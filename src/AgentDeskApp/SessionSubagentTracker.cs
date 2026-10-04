using System.Text.Json;

namespace AgentDeskApp;

/// <summary>
/// 1セッションの会話ログファイル(.jsonl)を増分読み込みし、その中で呼び出されている
/// サブエージェント(Agent toolの<c>subagent_type</c>)の稼働状況を追跡するクラス(design.md §6参照)。
/// 前回読んだバイト位置を覚えておき、追記分だけを読むことでファイル全体の再読み込みを避ける。
/// </summary>
public sealed class SessionSubagentTracker
{
    /// <summary>
    /// サブエージェント呼び出しに使われるツール名。実機確認では"Agent"だったが、
    /// Claude Codeのバージョンや、外部CLI・メインチャットセッション(headless "claude -p"等)経由での
    /// 呼び出しでは"Task"名義になるケースも確認されたため、両方を許容する(U-25)。
    /// </summary>
    private static readonly HashSet<string> SubagentToolNames = new(StringComparer.Ordinal) { "Agent", "Task" };

    private readonly IncrementalLineReader _reader = new();

    /// <summary>候補一覧が空・未接続だった警告を出したか(1トラッカーにつき1回だけ出す)。</summary>
    private bool _warnedEmptyCandidates;

    /// <summary>
    /// tool_use_id → (呼び出し中のサブエージェント名の一覧, バックグラウンド呼び出しかどうか)。
    /// 名前一覧はsubagent_typeに加え、汎用/不明なsubagent_typeの場合は命令文から検出したメンバーIdを含む(BUG-8)。
    /// バックグラウンド呼び出しは、対応するtool_resultが「受け付けました」という即時応答でしかなく
    /// 実際の完了を意味しないため(design.md §6.3)、tool_resultでは消さず、後述のtask-notification
    /// (status=completed)が来るまで稼働中とみなす。
    /// <see cref="Poll"/>はMainWindowのDispatcherTimer(logPollTimer)からUIスレッド上で
    /// 単一スレッド呼び出しされる前提のため、このDictionaryはlockで保護していない。
    /// 複数スレッドから呼ばれる構成に変更する場合は同期を追加すること。
    /// </summary>
    private readonly Dictionary<string, (IReadOnlyList<string> SubagentNames, bool IsBackground, DateTime StartedAt)> _pending = [];

    /// <summary>診断ログ(Debug出力)に出すセッション識別用の短縮ID。未設定なら空。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>診断ログの出力先。既定は共有ログ(<see cref="DiagnosticLog.Shared"/>)。</summary>
    public DiagnosticLog Log { get; set; } = DiagnosticLog.Shared;

    /// <summary>前回の<see cref="ConsumeCompletedNames"/>以降に完了(同期のtool_result・task-notification・追跡打ち切り)したサブエージェント名。</summary>
    private readonly List<string> _completedSinceLastConsume = [];

    /// <summary>直近の<see cref="Poll"/>で読んだ新規行数(診断用)。</summary>
    public int LastPollLineCount { get; private set; }

    /// <summary>
    /// 前回この関数を呼んで以降に完了したサブエージェント名を取り出し、内部の一覧を空にする。
    /// 同じPollでtool_useと完了通知が両方読まれ、<see cref="RunningSubagentNames"/>に一度も現れなかった
    /// 短時間の作業でも、呼び出し側が「作業を経由して完了した」と扱えるようにするためのもの。
    /// </summary>
    /// <returns>完了したサブエージェント名の一覧(重複あり得る)。</returns>
    public IReadOnlyList<string> ConsumeCompletedNames()
    {
        var names = _completedSinceLastConsume.ToList();
        _completedSinceLastConsume.Clear();
        return names;
    }

    /// <summary>pendingから1件取り除き、取り除けたら完了名の一覧へ記録する。</summary>
    /// <param name="toolUseId">取り除くtool_use_id。</param>
    private bool RemovePendingAsCompleted(string toolUseId)
    {
        if (!_pending.Remove(toolUseId, out var entry))
        {
            return false;
        }

        _completedSinceLastConsume.AddRange(entry.SubagentNames);
        return true;
    }

    /// <summary>
    /// 会話ログの現在の末尾から追跡を始める(初回発見前の古い呼び出し・完了通知で誤検知しないため)。
    /// 大きなログでも全読みせず、サイズを取得するだけで済む。
    /// </summary>
    /// <param name="transcriptFilePath">会話ログファイル(.jsonl)の絶対パス。</param>
    public void StartFromEnd(string transcriptFilePath) => _reader.SkipToEnd(transcriptFilePath);

    /// <summary>現時点で(このセッション内で)呼び出し中とみなされるサブエージェント名の集合。</summary>
    public IReadOnlySet<string> RunningSubagentNames => _pending.Values.SelectMany(v => v.SubagentNames).ToHashSet();

    /// <summary>完了通知待ちの非同期(バックグラウンド)サブエージェントが1件でもあるか(親セッションがidleでも追跡を続ける判断に使う)。同期呼び出しは親がidleなら必ず完了しているため含めない。</summary>
    public bool HasPendingAsync => _pending.Values.Any(v => v.IsBackground);

    /// <summary>現在時刻を返す関数(打ち切り判定用。テストで差し替える)。既定は <see cref="DateTime.Now"/>。</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>
    /// 非同期(バックグラウンド)サブエージェントの完了通知を待ち続ける最大時間。超えた呼び出しは打ち切る(安全弁)。
    /// 設定 AsyncSubagentMaxTrackMinutes から与える。
    /// </summary>
    public TimeSpan AsyncMaxTrackDuration { get; set; } = TimeSpan.FromMinutes(AppSettings.DefaultAsyncSubagentMaxTrackMinutes);

    /// <summary>
    /// 開始から <see cref="AsyncMaxTrackDuration"/> を超えた非同期サブエージェントを待ち状態から外す(安全弁)。
    /// task-notificationが届かないまま「作業中」が永久に残る事故を防ぐ。
    /// </summary>
    /// <param name="now">判定に使う現在時刻。</param>
    public void ExpireStaleAsync(DateTime now)
    {
        foreach (var id in _pending.Where(kv => kv.Value.IsBackground && now - kv.Value.StartedAt > AsyncMaxTrackDuration)
                     .Select(kv => kv.Key).ToList())
        {
            _pending.Remove(id);
            Log.Write($"subagent追跡を安全弁で打ち切り session={Label}");
        }
    }

    /// <summary>
    /// 命令文からのメンバー名検出(BUG-8)に使う既知メンバー一覧の取得デリゲート。
    /// 未設定(null)または空の場合は名前検出を行わず、subagent_typeのみで追跡する。
    /// </summary>
    public Func<IReadOnlyCollection<PromptNominationCandidate>?>? NominationCandidatesProvider { get; set; }

    /// <summary>
    /// 会話ログファイルの新規追記分を読み込み、内部状態を更新する。
    /// </summary>
    /// <param name="transcriptFilePath">会話ログファイル(.jsonl)の絶対パス。</param>
    public void Poll(string transcriptFilePath)
    {
        var newLines = _reader.ReadNewLines(transcriptFilePath);
        LastPollLineCount = newLines.Count;
        if (newLines.Count > 0)
        {
            Log.Write($"読了行数 session={Label} lines={newLines.Count}");
        }

        if (_reader.WasTruncated)
        {
            // ファイルが短くなっている(ローテーション等の異常系)場合は稼働中状態をクリアする。
            _pending.Clear();
        }

        foreach (var line in newLines)
        {
            ProcessLine(line);
        }

        ExpireStaleAsync(Clock());
    }

    /// <summary>会話ログ1行分を解析し、サブエージェント呼び出しの開始・終了を状態に反映する。</summary>
    private void ProcessLine(string line)
    {
        // バックグラウンド呼び出しの完了通知(<task-notification>...<status>completed</status>...)は、
        // JSON構造が呼び出し元によって微妙に異なる(queue-operationの直接contentだったり、
        // user メッセージのcontent文字列だったりする)ため、構造をパースせず行全体の文字列検索で
        // 実務的に拾う(design.md §6.3で判明した経緯を踏まえた対応)。
        if (line.Contains("<task-notification>") && IsTerminalStatus(line))
        {
            var completedToolUseId = ExtractBetween(line, "<tool-use-id>", "</tool-use-id>");
            if (completedToolUseId is not null)
            {
                if (RemovePendingAsCompleted(completedToolUseId))
                {
                    Log.Write($"task-notification完了 session={Label} pending={_pending.Count}");
                }
            }
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            // 非同期起動の判定(isAsync / status=async_launched)は行のルート直下のtoolUseResultにある。
            var isAsyncLaunchLine = doc.RootElement.ValueKind == JsonValueKind.Object &&
                                    doc.RootElement.TryGetProperty("toolUseResult", out var toolUseResult) &&
                                    IsAsyncLaunchResult(toolUseResult);

            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out var typeProp))
                {
                    continue;
                }

                switch (typeProp.GetString())
                {
                    case "tool_use" when IsSubagentToolUse(item, out var toolUseId, out var subagentNames, out var isBackground):
                        _pending[toolUseId] = (subagentNames, isBackground, Clock());
                        Log.Write($"tool_use検知 session={Label} names={string.Join(",", subagentNames)} background={isBackground} pending={_pending.Count}");
                        break;

                    case "tool_result" when item.TryGetProperty("tool_use_id", out var toolUseIdProp):
                        var id = toolUseIdProp.GetString() ?? string.Empty;
                        // バックグラウンド呼び出しのtool_resultは「起動を受け付けた」合図でしかないため無視する。
                        // (実際の完了はtask-notificationで判定する、上記コメント参照)
                        if (_pending.TryGetValue(id, out var pendingEntry) && !pendingEntry.IsBackground)
                        {
                            // 非同期起動(run_in_backgroundが無くても、Claudeデスクトップ等ではasync_launchedで返る)なら
                            // tool_resultで完了扱いにせず、task-notificationまで待つバックグラウンド扱いに切り替える。
                            if (isAsyncLaunchLine || item.GetRawText().Contains("Async agent launched", StringComparison.Ordinal))
                            {
                                _pending[id] = (pendingEntry.SubagentNames, true, Clock());
                                Log.Write($"async_launched確認 session={Label} names={string.Join(",", pendingEntry.SubagentNames)}");
                            }
                            else
                            {
                                RemovePendingAsCompleted(id);
                                Log.Write($"同期tool_result完了 session={Label} names={string.Join(",", pendingEntry.SubagentNames)} pending={_pending.Count}");
                            }
                        }
                        break;
                }
            }
        }
    }

    /// <summary>task-notificationの行が終了ステータス(completed/failed/killed/stopped)を含むか。</summary>
    /// <param name="line">会話ログ1行。</param>
    private static bool IsTerminalStatus(string line) =>
        line.Contains("<status>completed</status>") || line.Contains("<status>failed</status>") ||
        line.Contains("<status>killed</status>") || line.Contains("<status>stopped</status>");

    /// <summary>toolUseResultが非同期起動(isAsync=true または status=async_launched)を示すか。</summary>
    /// <param name="toolUseResult">行ルート直下のtoolUseResult要素。</param>
    private static bool IsAsyncLaunchResult(JsonElement toolUseResult)
    {
        if (toolUseResult.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return (toolUseResult.TryGetProperty("isAsync", out var a) && a.ValueKind == JsonValueKind.True) ||
               GetStringProperty(toolUseResult, "status") == "async_launched";
    }

    /// <summary>
    /// message.content配列中の1要素がサブエージェント呼び出し(Agent/Task)のtool_useかどうかを判定し、
    /// 該当する場合はtool_use_id・稼働中とみなす名前の一覧・バックグラウンド呼び出しかどうかを取り出す(BUG-8)。
    /// subagent_typeがあればそれを名前に含める(従来どおり)。subagent_typeが無い、または既知メンバーIdでない
    /// (general-purpose等の汎用)場合は、命令文(input.prompt/description)に含まれるメンバー名も名前に加える。
    /// </summary>
    /// <param name="item">content配列中の1要素(JSON要素)。</param>
    /// <param name="toolUseId">呼び出しのtool_use_id(判定失敗時は空文字列)。</param>
    /// <param name="subagentNames">稼働中とみなす名前の一覧(判定失敗時は空)。</param>
    /// <param name="isBackground">run_in_background=trueで呼び出されたかどうか。</param>
    private bool IsSubagentToolUse(JsonElement item, out string toolUseId, out IReadOnlyList<string> subagentNames, out bool isBackground)
    {
        toolUseId = string.Empty;
        subagentNames = [];
        isBackground = false;

        if (!item.TryGetProperty("name", out var nameProp) || !SubagentToolNames.Contains(nameProp.GetString() ?? string.Empty))
        {
            return false;
        }

        if (!item.TryGetProperty("id", out var idProp) ||
            !item.TryGetProperty("input", out var inputProp) || inputProp.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        toolUseId = idProp.GetString() ?? string.Empty;
        isBackground = inputProp.TryGetProperty("run_in_background", out var bgProp) &&
                       bgProp.ValueKind == JsonValueKind.True;

        var subagentType = GetStringProperty(inputProp, "subagent_type");
        var names = new List<string>();
        if (subagentType.Length > 0)
        {
            names.Add(subagentType);
        }

        var candidates = NominationCandidatesProvider?.Invoke();
        if (candidates is not { Count: > 0 } && !_warnedEmptyCandidates)
        {
            // 候補一覧が取れない(未接続・読み込み前)と命令文からの検知が働かないため、切り分け用に1度だけ知らせる
            _warnedEmptyCandidates = true;
            Log.Write($"警告 候補一覧が{(NominationCandidatesProvider is null ? "未接続(null)" : "空")} session={Label}");
        }

        if (candidates is { Count: > 0 } && !candidates.Any(c => string.Equals(c.Id, subagentType, StringComparison.Ordinal)))
        {
            var instruction = GetStringProperty(inputProp, "prompt") + "\n" + GetStringProperty(inputProp, "description");
            foreach (var memberId in PromptMemberNominationDetector.Detect(instruction, candidates))
            {
                if (!names.Contains(memberId))
                {
                    names.Add(memberId);
                }
            }
        }

        // どの名前も候補のIdに一致しなかった(=メンバーとして検知できなかった)場合は切り分け用に記録する。
        // プロンプト本文・説明文(description)は出さず、subagent_typeの値と候補件数だけにとどめる(会話内容を記録しない約束)。
        var matchedCandidate = candidates is not null && names.Any(n => candidates.Any(c => string.Equals(c.Id, n, StringComparison.Ordinal)));
        if (!matchedCandidate)
        {
            Log.Write($"Agent呼び出し検出 名前未一致 candidates={candidates?.Count ?? 0} subagent_type={subagentType}");
        }

        subagentNames = names;
        return toolUseId.Length > 0 && names.Count > 0;
    }

    /// <summary>JSONオブジェクトの文字列プロパティを取得する(存在しない・文字列でない場合は空文字列)。</summary>
    /// <param name="element">対象のJSONオブジェクト。</param>
    /// <param name="propertyName">プロパティ名。</param>
    private static string GetStringProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// 文字列中から開始・終了マーカーで挟まれた部分文字列を取り出す。
    /// どちらかのマーカーが見つからない場合はnullを返す。
    /// </summary>
    /// <param name="source">検索対象の文字列全体。</param>
    /// <param name="startMarker">開始マーカー文字列。</param>
    /// <param name="endMarker">終了マーカー文字列。</param>
    private static string? ExtractBetween(string source, string startMarker, string endMarker)
    {
        var startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += startMarker.Length;
        var endIndex = source.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        return endIndex < 0 ? null : source[startIndex..endIndex];
    }
}
