using System.Diagnostics;
using System.IO;
using System.Text;

namespace AgentDeskApp;

/// <summary>メンバー(サブエージェント)の状態表示に使う3段階(design.md §6)。</summary>
public enum MemberActivityState
{
    /// <summary>待機中(呼び出されていない、または未確認)。</summary>
    Idle,

    /// <summary>作業中(いずれかのセッションで呼び出し中と確認できた)。</summary>
    Running,

    /// <summary>直近まで作業中で、完了を確認した。</summary>
    Done,

    /// <summary>ユーザーの手動操作により処理が中断された(C-9+)。</summary>
    Cancelled,

    /// <summary>応答待ちタイムアウトにより処理が強制終了された(C-9+)。</summary>
    TimedOut,
}

/// <summary>
/// 1セッション分のリーダー情報(design.md §9)。<see cref="MemberActivityMonitor.GetLeaders"/>が返す。
/// </summary>
/// <param name="SessionId">セッションID(カードの状態更新をこのIDで追跡し続けるために使う)。</param>
/// <param name="State">そのセッションの現在の状態(待機/作業中/完了)。</param>
/// <param name="CurrentWorkPath">直近にRead/Edit/Write等で触った絶対パス(未追跡ならnull)。</param>
/// <param name="Engine">セッションのエンジン(Claude または Gemini)。</param>
/// <param name="LastActivityAt">
/// その会話で最後に動きがあった時刻(Claude: 作業中を確認した・作業ファイルが変わった時刻、Gemini: ログの更新時刻)。
/// 同じAIの会話が複数あるとき、リーダーカードに「いちばん新しい会話」の内容を出すために使う。不明なら既定値(最古扱い)。
/// </param>
public sealed record LeaderSessionInfo(
    string SessionId,
    MemberActivityState State,
    string? CurrentWorkPath,
    AgentEngineKind Engine = AgentEngineKind.Claude,
    DateTime LastActivityAt = default);

/// <summary>
/// メンバーの稼働状況を2段階のポーリングで追跡するクラス(design.md §6)。
/// - 30秒周期: <see cref="RefreshBusySessions"/>で<c>claude agents --json</c>の結果を渡し、
///   busyな対話セッションだけを追跡対象にする(重いプロセス起動の頻度を抑えるため)。
/// - 5秒周期: <see cref="PollTrackedSessions"/>で、追跡対象セッションの会話ログを増分読み込みし、
///   稼働中のサブエージェント名を再計算する。
/// WPFに依存しないロジック層として実装し、呼び出し(タイマー起動等)はMainWindow側が担当する。
/// </summary>
public sealed class MemberActivityMonitor
{
    private readonly Dictionary<string, SessionSubagentTracker> _trackers = [];
    private readonly Dictionary<string, string> _cwdBySessionId = [];
    private readonly Dictionary<string, MemberActivityState> _states = [];
    private HashSet<string> _previouslyRunning = [];

    // リーダー(セッション自体)の状態はsessionId単位で持つ。同じフォルダに複数セッションが
    // いる場合(design.md §9のパターンB)、フォルダ単位に丸めてしまうと1人分しか表現できないため。
    private readonly Dictionary<string, MemberActivityState> _leaderStatesBySessionId = [];
    private readonly Dictionary<string, string> _leaderCwdBySessionId = [];

    /// <summary>セッションIDごとの、最後に動き(作業中の確認・作業ファイルの変化)があった時刻。</summary>
    private readonly Dictionary<string, DateTime> _leaderLastActivityBySessionId = [];
    private HashSet<string> _previouslyRunningLeaderSessionIds = [];

    private readonly Dictionary<string, SessionWorkspaceTracker> _workspaceTrackers = [];
    private readonly Dictionary<string, string> _workspaceCwdBySessionId = [];
    private readonly Dictionary<string, string> _currentWorkPathBySessionId = [];

    // BUG-1: タイマー(UI)・実行ウィンドウのコールバック・将来のバックグラウンド呼び出しが
    // 同じ辞書を同時に触ってもInvalidOperationExceptionにならないよう、状態の読み書きは全てこのロックで直列化する。
    private readonly object _gate = new();

    /// <summary>メンバー名(frontmatterのname)ごとの現在の状態(呼び出し時点のスナップショット。列挙中に書き換わらない)。</summary>
    public IReadOnlyDictionary<string, MemberActivityState> States
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, MemberActivityState>(_states);
            }
        }
    }

    /// <summary>
    /// Agent/Taskの命令文からのメンバー名検出(BUG-8)に使う既知メンバー一覧の取得デリゲート。
    /// 未設定(null)の場合は名前検出を行わない。MainWindowが読み込み済みエージェントを返す。
    /// </summary>
    public Func<IReadOnlyCollection<PromptNominationCandidate>>? NominationCandidatesProvider { get; set; }

    /// <summary>
    /// Geminiセッションの外部スキャナーデリゲート(単体テストでのモック化・差し替え用)。
    /// </summary>
    public Func<int, List<GeminiSessionRecord>>? GeminiScanner { get; set; }

    /// <summary>メンバーの作業状態の検知でGeminiセッションを走査する範囲(分)。「会話N件」の範囲とは別で、3時間(180分)固定。</summary>
    public const int GeminiMemberStateWindowMinutes = 180;

    /// <summary>Geminiの会話を「会話N件」に数える「直近」とみなす分数(設定 GeminiRecentConversationMinutes)。</summary>
    public int GeminiRecentMinutes { get; set; } = AppSettings.DefaultGeminiRecentConversationMinutes;

    /// <summary>Claudeの待機中の会話を「直近」とみなす分数(設定 ClaudeRecentConversationMinutes)。</summary>
    public int ClaudeRecentIdleMinutes { get; set; } = AppSettings.DefaultClaudeRecentConversationMinutes;

    /// <summary>
    /// 会話ログ(jsonl)の最終更新時刻を返す関数(単体テストで差し替える用)。引数は(cwd, sessionId)。ファイルが無ければnull。
    /// </summary>
    public Func<string, string, DateTime?> TranscriptLastWriteProvider { get; set; } = (cwd, sessionId) =>
    {
        try
        {
            var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            return File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    };

    /// <summary>
    /// 現在時刻を返す関数(リーダーの会話の新しさの記録に使う)。単体テストで時刻を差し替えるためのもの。既定は <see cref="DateTime.Now"/>。
    /// </summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>診断ログの出力先。既定は共有ログ(<see cref="DiagnosticLog.Shared"/>)。テストでは一時ファイルのログへ差し替える。</summary>
    public DiagnosticLog Log { get; set; } = DiagnosticLog.Shared;

    /// <summary>
    /// 作業中(Running)になったメンバーを、完了通知後も最低この秒数は作業中表示に保つ(設定 MinRunningDisplaySeconds)。
    /// 短い作業でもカードの揺れ・完了の光が目視できるようにする。0なら最短表示なし。
    /// クラス単体の既定は0(最短表示なし)で、アプリでは MainWindow が設定値(既定3秒)を与える。
    /// </summary>
    public int MinRunningDisplaySeconds { get; set; }

    /// <summary>メンバーごとの、Runningとして初めて表示した時刻(最短表示時間の計算用。Doneにしたら消す)。</summary>
    private readonly Dictionary<string, DateTime> _runningSince = [];

    /// <summary>完了を検知済みだが、最短表示時間が過ぎるまでRunning表示を保っているメンバー名。</summary>
    private readonly HashSet<string> _heldRunning = [];

    /// <summary>非同期サブエージェントの完了通知を待ち続ける最大分数(設定 AsyncSubagentMaxTrackMinutes)。</summary>
    public int AsyncSubagentMaxTrackMinutes { get; set; } = AppSettings.DefaultAsyncSubagentMaxTrackMinutes;

    /// <summary>
    /// 周期の処理。対話セッション(busy/idle問わず)全てを追跡対象にし、一覧から消えたセッションは
    /// (完了待ちの非同期サブエージェントが無ければ)追跡をやめる。
    /// </summary>
    /// <param name="sessions"><c>claude agents --json</c>の解析結果。</param>
    public void RefreshBusySessions(IReadOnlyList<AgentSessionInfo> sessions)
    {
        lock (_gate)
        {
            RefreshBusySessionsCore(sessions);
        }
    }

    private void RefreshBusySessionsCore(IReadOnlyList<AgentSessionInfo> sessions)
    {

        // claude agents --json の取得結果の要約(変化があったときだけ出す)。tool_use検知より前に
        // 「そもそも対話セッションが一覧に載っているか」を切り分けるための診断。
        Log.WriteOnChange("agents", "agents取得 " + SummarizeSessions(sessions));

        // U-25: 外部CLIやメインチャットセッションから "claude --bg"・"claude -p" 等で
        // 起動されたバックグラウンドセッションも追跡対象に含める。対話セッションのような
        // busyフラグを持たないため、終了を示すState(stopped/completed/failed等)が
        // 確認できるまでは稼働中とみなして追跡を続ける。
        var backgroundSessions = sessions
            .Where(s => s.Kind == "background" && !IsBackgroundSessionFinished(s.State))
            .ToList();

        // 親セッションがbusyなのはサブエージェント呼び出しの前後の短い間だけで、周期の更新に当たらないと
        // tool_use行を読む機会が無くなる。そのため busy/idle を問わず、対話セッション全てを追跡対象にする。
        var interactiveSessions = sessions.Where(s => s.Kind == "interactive").ToList();
        var trackableSessions = interactiveSessions.Concat(backgroundSessions).ToList();
        var trackableSessionIds = trackableSessions.Select(s => s.SessionId).ToHashSet();

        foreach (var session in trackableSessions)
        {
            _cwdBySessionId[session.SessionId] = session.Cwd;
            if (_trackers.ContainsKey(session.SessionId))
            {
                continue;
            }

            var tracker = new SessionSubagentTracker
            {
                NominationCandidatesProvider = () => NominationCandidatesProvider?.Invoke(),
                Clock = () => Clock(),
                Label = ShortId(session.SessionId),
                Log = Log,
            };

            // busyで初めて見えたセッション・backgroundは従来どおり先頭から読む。
            // idleで初めて見えた対話セッションは、過去の行で誤検知しないよう末尾から追跡を始める。
            var fromEnd = session.Kind == "interactive" && !session.IsBusy;
            if (fromEnd)
            {
                try
                {
                    tracker.StartFromEnd(ClaudeTranscriptPathResolver.Resolve(session.Cwd, session.SessionId));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 末尾位置を取れなくても追跡自体は続ける(次回以降の追記を読む)
                }
            }

            _trackers[session.SessionId] = tracker;
            Log.Write($"セッション追跡開始 session={tracker.Label} kind={session.Kind} status={session.Status} fromEnd={fromEnd} trackers={_trackers.Count}");
        }

        // 非同期サブエージェントが未完了(pending)の間は、親がidleや一覧から消えても追跡を続ける。
        // 打ち切りは安全弁(最大追跡時間)で行い、pendingが空になったら通常どおり破棄する。
        var maxTrack = TimeSpan.FromMinutes(AsyncSubagentMaxTrackMinutes > 0 ? AsyncSubagentMaxTrackMinutes : AppSettings.DefaultAsyncSubagentMaxTrackMinutes);
        foreach (var tracker in _trackers.Values)
        {
            tracker.AsyncMaxTrackDuration = maxTrack;
        }

        foreach (var sessionId in _trackers.Keys.Where(id => !trackableSessionIds.Contains(id)).ToList())
        {
            _trackers[sessionId].ExpireStaleAsync(Clock());
            if (_trackers[sessionId].HasPendingAsync)
            {
                continue;
            }

            Log.Write($"セッション追跡終了 session={ShortId(sessionId)} trackers={_trackers.Count - 1}");
            _trackers.Remove(sessionId);
            _cwdBySessionId.Remove(sessionId);
        }

        RefreshLeaders(sessions);
    }

    /// <summary>
    /// <c>claude agents --json</c>の結果を、対話セッション数・各statusの件数だけに要約する(診断ログ用。個人情報は含めない)。
    /// </summary>
    /// <param name="sessions">取得したセッション一覧。</param>
    /// <returns>例: "total=2 interactive=2 [busy=1,idle=1] background=0"。</returns>
    internal static string SummarizeSessions(IReadOnlyList<AgentSessionInfo> sessions)
    {
        var interactive = sessions.Where(s => s.Kind == "interactive").ToList();
        var statuses = string.Join(",", interactive
            .GroupBy(s => s.Status ?? "(none)")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}={g.Count()}"));
        var background = sessions.Count(s => s.Kind == "background");
        return $"total={sessions.Count} interactive={interactive.Count} [{statuses}] background={background}";
    }

    /// <summary>診断ログ用にセッションIDを先頭8文字へ短縮する。</summary>
    private static string ShortId(string sessionId) => sessionId.Length > 8 ? sessionId[..8] : sessionId;

    /// <summary>バックグラウンドセッションのStateが「終了済み」を意味するかどうか(U-25)。</summary>
    private static bool IsBackgroundSessionFinished(string? state) =>
        state is not null &&
        (state.Equals("stopped", StringComparison.OrdinalIgnoreCase) ||
         state.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
         state.Equals("failed", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// グループ/チームの「リーダーカード」用に、対話セッション全体(busy/idle問わず)のcwdを見て
    /// 「どのセッションが今どこにいて、busyかどうか」を更新する(design.md §9)。
    /// 同じフォルダに複数セッションがいてもそれぞれ別人として扱えるよう、sessionId単位で持つ。
    /// メンバーと違いサブエージェント呼び出しの追跡は不要で、セッション自体のstatusだけで判定できる。
    /// </summary>
    private void RefreshLeaders(IReadOnlyList<AgentSessionInfo> sessions)
    {
        var interactiveSessions = sessions.Where(s => s.Kind == "interactive").ToList();
        var presentSessionIds = interactiveSessions.Select(s => s.SessionId).ToHashSet();

        var now = Clock();
        foreach (var session in interactiveSessions)
        {
            // 初めて見えたセッションは、見えた時刻を最後の動きとして記録しておく
            _leaderLastActivityBySessionId.TryAdd(session.SessionId, now);
            _leaderCwdBySessionId[session.SessionId] = session.Cwd;
            _workspaceCwdBySessionId[session.SessionId] = session.Cwd;
            _workspaceTrackers.TryAdd(session.SessionId, new SessionWorkspaceTracker());
        }

        // 一覧から消えた(=セッションが閉じられた)ものは、状態・作業パス・追跡ともに後始末する。
        foreach (var sessionId in _leaderCwdBySessionId.Keys.Where(id => !presentSessionIds.Contains(id)).ToList())
        {
            _leaderCwdBySessionId.Remove(sessionId);
            _leaderStatesBySessionId.Remove(sessionId);
            _leaderLastActivityBySessionId.Remove(sessionId);
        }

        foreach (var sessionId in _workspaceTrackers.Keys.Where(id => !presentSessionIds.Contains(id)).ToList())
        {
            _workspaceTrackers.Remove(sessionId);
            _workspaceCwdBySessionId.Remove(sessionId);
            _currentWorkPathBySessionId.Remove(sessionId);
        }

        var currentlyRunningSessionIds = interactiveSessions.Where(s => s.IsBusy).Select(s => s.SessionId).ToHashSet();

        foreach (var sessionId in currentlyRunningSessionIds)
        {
            _leaderStatesBySessionId[sessionId] = MemberActivityState.Running;
            _leaderLastActivityBySessionId[sessionId] = now;
        }

        foreach (var sessionId in _previouslyRunningLeaderSessionIds.Except(currentlyRunningSessionIds))
        {
            if (presentSessionIds.Contains(sessionId))
            {
                _leaderStatesBySessionId[sessionId] = MemberActivityState.Done;
            }
        }

        _previouslyRunningLeaderSessionIds = currentlyRunningSessionIds;
    }

    /// <summary>
    /// 指定フォルダがcwdの対話セッション(=リーダー)のうち、作業中または完了直後(Running/Done)のものだけを返す。
    /// 同じフォルダに複数セッションがいれば、その数だけ要素が返る(design.md §9のパターンB)。
    /// **一度もbusyになっていない(Idleのままの)セッションはカード化しない**(2026-09-26改訂)。
    /// 実機確認で、開けっぱなしの旧ウィンドウが何枚も「待機」カードとして残り続けて見づらいことが
    /// 判明したため、待機中のみのセッションは表示しない方針にした。
    /// </summary>
    public IReadOnlyList<LeaderSessionInfo> GetLeaders(string folderPath)
    {
        var normalized = NormalizePath(folderPath);
        List<LeaderSessionInfo> list;
        lock (_gate)
        {
            list = _leaderCwdBySessionId
                .Where(kv => IsSameOrSubPath(NormalizePath(kv.Value), normalized))
                .Select(kv => new LeaderSessionInfo(
                    kv.Key,
                    _leaderStatesBySessionId.GetValueOrDefault(kv.Key, MemberActivityState.Idle),
                    _currentWorkPathBySessionId.GetValueOrDefault(kv.Key),
                    AgentEngineKind.Claude,
                    _leaderLastActivityBySessionId.GetValueOrDefault(kv.Key)))
                .Where(leader => leader.State != MemberActivityState.Idle)
                .ToList();
        }

        // Gemini (Antigravity) のアクティブセッションも結合
        if (GeminiScanner != null)
        {
            try
            {
                var geminiSessions = GeminiScanner(GeminiRecentMinutes);
                foreach (var g in geminiSessions)
                {
                    if (g.State == MemberActivityState.Idle)
                    {
                        continue;
                    }

                    // CwdまたはLastWorkPathの所属フォルダが指定フォルダに合致する場合に含める
                    var geminiPath = !string.IsNullOrEmpty(g.Cwd) ? g.Cwd : g.LastWorkPath;
                    if (!string.IsNullOrEmpty(geminiPath))
                    {
                        var normGemini = NormalizePath(geminiPath);
                        if (IsSameOrSubPath(normGemini, normalized))
                        {
                            list.Add(new LeaderSessionInfo(
                                SessionId: g.ConversationId.Length > 8 ? g.ConversationId[..8] : g.ConversationId,
                                State: g.State,
                                CurrentWorkPath: g.LastWorkPath,
                                Engine: AgentEngineKind.Gemini,
                                LastActivityAt: g.LastModified));
                        }
                    }
                }
            }
            catch
            {
                // Geminiセッションスキャンの失敗は安全に無視
            }
        }

        return list;
    }

    /// <summary>
    /// 指定フォルダ配下の、待機中(作業中でも完了直後でもない)だが会話ログが直近(<see cref="ClaudeRecentIdleMinutes"/>分以内)に
    /// 更新されているClaudeセッションのIDを返す。リーダーカードの「会話N件」に足すためだけに使い、
    /// 「作業中」の数や状態表示には影響しない(旧ウィンドウが待機カードで溢れないよう、古いものは数えない)。
    /// </summary>
    /// <param name="folderPath">対象フォルダ。</param>
    /// <returns>直近に会話があった待機中セッションのID一覧。</returns>
    public IReadOnlyList<string> GetRecentIdleLeaderSessionIds(string folderPath)
    {
        var normalized = NormalizePath(folderPath);
        List<KeyValuePair<string, string>> candidates;
        lock (_gate)
        {
            candidates = _leaderCwdBySessionId
                .Where(kv => IsSameOrSubPath(NormalizePath(kv.Value), normalized))
                .Where(kv => _leaderStatesBySessionId.GetValueOrDefault(kv.Key, MemberActivityState.Idle) == MemberActivityState.Idle)
                .ToList();
        }

        // ファイルアクセスはロックの外で行う
        var threshold = Clock() - TimeSpan.FromMinutes(ClaudeRecentIdleMinutes);
        return candidates
            .Where(kv => TranscriptLastWriteProvider(kv.Value, kv.Key) is { } t && t >= threshold)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>パス比較用の正規化(末尾の区切り文字を除去し、大文字小文字を無視する)。</summary>
    private static string NormalizePath(string path) => path.TrimEnd('\\', '/').ToLowerInvariant();

    /// <summary>
    /// 正規化済みパス同士が完全一致、またはどちらかがもう一方の配下(サブフォルダ)かどうかを判定する。
    /// 単純な文字列StartsWithだと "C:\Foo" が "C:\FooBar" に誤一致するため、区切り文字境界で比較する。
    /// </summary>
    private static bool IsSameOrSubPath(string normalizedA, string normalizedB)
    {
        if (normalizedA == normalizedB)
        {
            return true;
        }

        return normalizedA.StartsWith(normalizedB + "\\", StringComparison.Ordinal)
            || normalizedB.StartsWith(normalizedA + "\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// 5秒周期の処理。追跡対象セッションそれぞれの会話ログを増分読み込みし、
    /// 稼働中サブエージェント名の集合から各メンバーの状態を更新する。
    /// 1セッションの読み込み失敗(ログの一時的なロック等)が他のセッションや以後の周期を止めないよう、
    /// セッション単位で例外を握って継続する(BUG-1)。
    /// </summary>
    public void PollTrackedSessions()
    {
        // Geminiのスキャンはファイル走査で重いため、ロックの外で先に実行しておく。
        var geminiSessions = ScanGeminiSessionsSafely();

        lock (_gate)
        {
            foreach (var (sessionId, tracker) in _trackers.ToList())
            {
                try
                {
                    if (_cwdBySessionId.TryGetValue(sessionId, out var cwd))
                    {
                        tracker.Poll(ClaudeTranscriptPathResolver.Resolve(cwd, sessionId));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // このセッションは今回の周期だけ諦める(次の周期で再試行される)
                }
            }

            var currentlyRunning = _trackers.Values.SelectMany(t => t.RunningSubagentNames).ToHashSet();
            var completedNames = _trackers.Values.SelectMany(t => t.ConsumeCompletedNames()).ToHashSet();
            var now = Clock();

            // 作業中: 追跡で呼び出し中と確認できたメンバー
            foreach (var name in currentlyRunning)
            {
                if (!_previouslyRunning.Contains(name) && !_heldRunning.Contains(name))
                {
                    _runningSince[name] = now;
                }

                _heldRunning.Remove(name);
                SetState(name, MemberActivityState.Running);
            }

            // 完了: 前回Runningだったが今回は呼び出し中でないメンバー
            foreach (var name in _previouslyRunning.Except(currentlyRunning))
            {
                _heldRunning.Add(name);
            }

            // 同じPollでtool_useと完了通知が両方読まれ、Runningを一度も観測できなかった短時間の作業も、
            // 「Runningを経由した」として扱い、必ずRunning→Doneの遷移を起こす。
            foreach (var name in completedNames.Where(n => !currentlyRunning.Contains(n) && !_heldRunning.Contains(n)))
            {
                Log.Write($"メンバー完了をRunning未観測のまま検知(短時間作業) name={name}");
                _runningSince.TryAdd(name, now);
                _heldRunning.Add(name);
                SetState(name, MemberActivityState.Running);
            }

            // 最短表示時間が過ぎたものからDoneにする(過ぎるまではRunning表示を保つ)
            var minRunning = TimeSpan.FromSeconds(Math.Max(0, MinRunningDisplaySeconds));
            foreach (var name in _heldRunning.ToList())
            {
                if (!_runningSince.TryGetValue(name, out var since) || now - since >= minRunning)
                {
                    _heldRunning.Remove(name);
                    _runningSince.Remove(name);
                    SetState(name, MemberActivityState.Done);
                }
                else
                {
                    SetState(name, MemberActivityState.Running);
                }
            }

            _previouslyRunning = currentlyRunning;
            var claudeActive = currentlyRunning.Union(_heldRunning).ToHashSet();

            foreach (var (sessionId, tracker) in _workspaceTrackers.ToList())
            {
                try
                {
                    if (!_workspaceCwdBySessionId.TryGetValue(sessionId, out var cwd))
                    {
                        continue;
                    }

                    tracker.Poll(ClaudeTranscriptPathResolver.Resolve(cwd, sessionId));

                    if (tracker.CurrentPath is not null)
                    {
                        // 作業ファイルが変わったら、その会話に動きがあったとみなす
                        if (!string.Equals(_currentWorkPathBySessionId.GetValueOrDefault(sessionId), tracker.CurrentPath, StringComparison.Ordinal))
                        {
                            _leaderLastActivityBySessionId[sessionId] = Clock();
                        }

                        _currentWorkPathBySessionId[sessionId] = tracker.CurrentPath;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // 同上: 次の周期で再試行
                }
            }

            // 個別直接起動プロセスの監視(UIから直接起動されたエージェント)
            foreach (var (name, proc) in _directProcesses.ToList())
            {
                try
                {
                    if (proc.HasExited)
                    {
                        _directProcesses.Remove(name);
                        _states[name] = MemberActivityState.Done;
                    }
                    else
                    {
                        _states[name] = MemberActivityState.Running;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 実行ウィンドウ側でProcessがDisposeされた等でHasExitedを読めない=もう動いていないので完了扱いにする
                    _directProcesses.Remove(name);
                    _states[name] = MemberActivityState.Done;
                }
            }

            ApplyGeminiSessions(geminiSessions, claudeActive);
        }
    }

    /// <summary>
    /// メンバーの状態を更新し、変化があれば診断ログに「旧→新」を出す(ロック取得済みで呼ぶこと)。
    /// </summary>
    /// <param name="name">メンバー名。</param>
    /// <param name="state">新しい状態。</param>
    private void SetState(string name, MemberActivityState state)
    {
        var old = _states.GetValueOrDefault(name, MemberActivityState.Idle);
        _states[name] = state;
        if (old != state)
        {
            Log.Write($"メンバー状態 name={name} {old}->{state}");
        }
    }

    /// <summary>前回の周期でGeminiセッション由来でRunningにしたメンバー名(完了=Done遷移の検知用)。</summary>
    private HashSet<string> _previouslyRunningByGemini = [];

    /// <summary>
    /// Gemini (Antigravity) のアクティブセッションからサブエージェント稼働状態を取り込む(ロック取得済みで呼ぶこと)。
    /// 同じメンバーが複数セッションに現れる場合はRunningを優先する。前回Gemini由来でRunningだったメンバーが
    /// 今回どのセッションでもRunningでなくなった(セッションが時間枠外に出た場合を含む)ときはDoneにし、
    /// Runningのまま戻らない状態を防ぐ。
    /// </summary>
    /// <param name="geminiSessions">今回の周期で走査したGeminiセッション一覧。</param>
    /// <param name="claudeRunning">Claude側の追跡でRunning中のメンバー名(こちらを優先し、Geminiの状態で上書きしない)。</param>
    private void ApplyGeminiSessions(List<GeminiSessionRecord> geminiSessions, HashSet<string> claudeRunning)
    {
        var candidates = NominationCandidatesProvider?.Invoke() ?? [];
        var geminiStates = new Dictionary<string, MemberActivityState>();

        foreach (var g in geminiSessions)
        {
            foreach (var subName in ResolveGeminiMemberNames(g, candidates))
            {
                if (!geminiStates.TryGetValue(subName, out var existing) || existing != MemberActivityState.Running)
                {
                    geminiStates[subName] = g.State;
                }
            }
        }

        var runningNow = new HashSet<string>();
        foreach (var (subName, state) in geminiStates)
        {
            // Claudeまたは直接起動で現在Running中でない限り、Geminiの状態(Running/Done)を反映
            if (claudeRunning.Contains(subName) || _directProcesses.ContainsKey(subName))
            {
                continue;
            }

            _states[subName] = state;
            if (state == MemberActivityState.Running)
            {
                runningNow.Add(subName);
            }
        }

        foreach (var subName in _previouslyRunningByGemini.Except(runningNow))
        {
            if (!claudeRunning.Contains(subName) && !_directProcesses.ContainsKey(subName) &&
                _states.GetValueOrDefault(subName) == MemberActivityState.Running)
            {
                _states[subName] = MemberActivityState.Done;
            }
        }

        _previouslyRunningByGemini = runningNow;
    }

    /// <summary>
    /// Geminiセッション1件から、状態を反映すべきメンバー名を求める。
    /// - スキルファイルのパスや invoke_subagent の TypeName 由来の名前(<see cref="GeminiSessionRecord.ActiveSubagents"/>)は従来どおり使う。
    ///   TypeName が既知メンバーのIdと大文字小文字違いで一致する場合は、登録側の表記に揃える。
    /// - TypeName が既知メンバーでない("self"・"research"などの汎用の型)場合は、Role を
    ///   <see cref="SubagentRoleMatcher.Match"/>で完全一致照合し、一致したメンバーを加える(一致しなければ何もしない)。
    /// </summary>
    /// <param name="session">Geminiセッションの走査結果。</param>
    /// <param name="candidates">照合対象の既知メンバー一覧(未設定なら空)。</param>
    public static HashSet<string> ResolveGeminiMemberNames(GeminiSessionRecord session, IReadOnlyCollection<PromptNominationCandidate> candidates)
    {
        var names = new HashSet<string>();
        foreach (var raw in session.ActiveSubagents ?? [])
        {
            names.Add(SubagentRoleMatcher.MatchTypeName(raw, candidates) ?? raw);
        }

        foreach (var invocation in session.SubagentInvocations ?? [])
        {
            if (SubagentRoleMatcher.MatchTypeName(invocation.TypeName, candidates) is not null)
            {
                // メンバー名そのもので呼ばれた → ActiveSubagents 側で反映済み
                continue;
            }

            var byRole = SubagentRoleMatcher.Match(invocation.Role, candidates);
            if (byRole is not null)
            {
                names.Add(byRole);
            }
        }

        return names;
    }

    /// <summary>Geminiセッションを走査する。失敗時は空リストを返し監視全体を止めない。</summary>
    private List<GeminiSessionRecord> ScanGeminiSessionsSafely()
    {
        if (GeminiScanner == null)
        {
            return [];
        }

        try
        {
            return GeminiScanner(GeminiMemberStateWindowMinutes);
        }
        catch
        {
            // Geminiセッションスキャンの失敗は安全に無視
            return [];
        }
    }

    private readonly Dictionary<string, Process> _directProcesses = [];

    /// <summary>
    /// ユーザーが画面(Msg等)から直接起動したエージェントプロセスを追跡登録する。
    /// プロセスが生存している間はActive、終了するとDoneに遷移する。
    /// </summary>
    public void RegisterDirectLaunch(string memberName, Process process)
    {
        lock (_gate)
        {
            _directProcesses[memberName] = process;
            _states[memberName] = MemberActivityState.Running;
        }
    }

    /// <summary>
    /// ユーザーの手動中断やタイムアウトなど、プロセスの生死監視だけでは区別できない終了理由を
    /// 明示的に反映する(C-9+)。旧ワンショット実行画面(2026-10-03削除済み)から呼び出していた。
    /// 追跡中の直接起動プロセスがあれば追跡対象から外し、以後のポーリングで通常のDone判定に
    /// 上書きされないようにする。
    /// </summary>
    /// <param name="memberName">対象メンバー名。</param>
    /// <param name="outcome">反映する終了状態(Cancelled/TimedOut等)。</param>
    public void ReportDirectLaunchOutcome(string memberName, MemberActivityState outcome)
    {
        lock (_gate)
        {
            _directProcesses.Remove(memberName);
            _states[memberName] = outcome;
        }
    }

    /// <summary>指定した名前の現在の状態を返す。未追跡ならIdle。</summary>
    public MemberActivityState GetState(string memberName)
    {
        lock (_gate)
        {
            if (_directProcesses.TryGetValue(memberName, out var proc))
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        return MemberActivityState.Running;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Dispose済み等でHasExitedを読めない場合は終了扱い
                }

                _directProcesses.Remove(memberName);
                _states[memberName] = MemberActivityState.Done;
                return MemberActivityState.Done;
            }

            return _states.GetValueOrDefault(memberName, MemberActivityState.Idle);
        }
    }

    /// <summary>指定したセッションIDのリーダー状態を返す。未追跡ならIdle(5秒周期のバッジ更新から使う)。</summary>
    public MemberActivityState GetLeaderSessionState(string sessionId)
    {
        lock (_gate)
        {
            return _leaderStatesBySessionId.GetValueOrDefault(sessionId, MemberActivityState.Idle);
        }
    }

    /// <summary>
    /// <paramref name="claudeCliPath"/>で指定されたClaude CLIの<c>agents --json</c>を非同期に実行し、
    /// 結果を解析して返す(30秒周期のタイマーから呼ぶ想定)。
    /// - 改善4: PATHが通っていない環境でも動作するよう、コマンド名"claude"固定ではなく、
    ///   <see cref="CliPathResolver"/>で解決済みの実行パスを呼び出し元から受け取る。
    /// - 改善5: 標準出力の読み取り・プロセス終了待機を非同期APIにし、UIスレッドをブロックしないようにする。
    /// </summary>
    /// <param name="claudeCliPath">実行するClaude CLIのコマンド名またはフルパス(<see cref="AppSettings.EffectiveClaudeCliPath"/>を想定)。</param>
    public static async Task<IReadOnlyList<AgentSessionInfo>> RunAgentsJsonCommandAsync(string claudeCliPath)
    {
        try
        {
            var startInfo = CliAvailability.CreateCliCommandStartInfo(claudeCliPath, "agents --json");
            startInfo.RedirectStandardOutput = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();

            // 従来のProcess.WaitForExit(5000)と同等のタイムアウト付き非同期待機(改善5)。
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5000));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // タイムアウト時、プロセスツリー(cmd.exe + node等の子プロセス)を強制終了しないと、
                // ログイン切れ等でCLIがハングしたまま残り続け、outputTask(ReadToEndAsync)も
                // 標準出力が閉じないため永遠にブロックされる(Q-1)。未ログイン環境で30秒周期の
                // タイマーごとにプロセスが増殖するのを防ぐため、即座にkillして空リストを返す。
                try { process.Kill(entireProcessTree: true); } catch { }
                DiagnosticLog.Shared.WriteOnChange("agents-error", "agents取得失敗(タイムアウト)");
                return [];
            }

            var output = await outputTask;
            return AgentsJsonClient.Parse(output);
        }
        catch (Exception ex)
        {
            // 例外の種類だけ出す(メッセージにパスが含まれうるため)
            DiagnosticLog.Shared.WriteOnChange("agents-error", $"agents取得失敗(例外 {ex.GetType().Name})");
            return [];
        }
    }
}
