using System.IO;

namespace AgentDeskApp;

/// <summary>
/// TEAM欄のリーダーカード1枚分(1つのAI＝エンジンにつき1枚)に出す内容。
/// 同じAIの会話(セッション)が複数あっても1枚にまとめた結果を表す。
/// </summary>
/// <param name="Engine">AI(Claude または Gemini)。</param>
/// <param name="State">カードに出す状態。どれか1つでも作業中なら作業中、作業中が無く完了直後があれば完了、会話が無ければ待機。</param>
/// <param name="LatestSession">いちばん新しい会話(会話が無ければnull)。</param>
/// <param name="SessionCount">まとめた会話の件数。</param>
public sealed record LeaderCardSummary(
    AgentEngineKind Engine,
    MemberActivityState State,
    LeaderSessionInfo? LatestSession,
    int SessionCount)
{
    /// <summary>リーダーカードの同梱アバター画像のファイル名(Claude)。テンプレートのIdと衝突しない名前にしている。</summary>
    public const string ClaudeAvatarFileName = "leader-claude.jpg";

    /// <summary>リーダーカードの同梱アバター画像のファイル名(Gemini)。テンプレートのIdと衝突しない名前にしている。</summary>
    public const string GeminiAvatarFileName = "leader-gemini.jpg";

    /// <summary>
    /// 指定したAIの会話一覧を1枚分の内容にまとめる。
    /// 他のAIの会話が混ざっていても、指定したAIの会話だけを対象にする。
    /// </summary>
    /// <param name="sessions">会話(リーダーセッション)の一覧。</param>
    /// <param name="engine">まとめる対象のAI。</param>
    /// <param name="recentIdleCount">
    /// 待機中だが直近に会話があったセッションの数(「会話N件」にだけ足す。状態・作業内容には影響しない)。
    /// </param>
    /// <returns>まとめた結果。対象の会話が無ければ待機(Idle)・会話0件(直近の待機会話があれば件数のみ加算)。</returns>
    public static LeaderCardSummary Summarize(IEnumerable<LeaderSessionInfo> sessions, AgentEngineKind engine, int recentIdleCount = 0)
    {
        var mine = sessions.Where(s => s.Engine == engine).ToList();
        recentIdleCount = Math.Max(0, recentIdleCount);
        if (mine.Count == 0)
        {
            return new LeaderCardSummary(engine, MemberActivityState.Idle, null, recentIdleCount);
        }

        var state = mine.Any(s => s.State == MemberActivityState.Running)
            ? MemberActivityState.Running
            : mine.Any(s => s.State == MemberActivityState.Done)
                ? MemberActivityState.Done
                : MemberActivityState.Idle;

        // いちばん新しい会話。時刻が同じなら作業中を優先し、それでも同じならセッションIDの順で決める(周期ごとに表示が入れ替わらないように)
        var latest = mine
            .OrderByDescending(s => s.LastActivityAt)
            .ThenBy(s => s.State == MemberActivityState.Running ? 0 : 1)
            .ThenBy(s => s.SessionId, StringComparer.Ordinal)
            .First();

        return new LeaderCardSummary(engine, state, latest, mine.Count + recentIdleCount);
    }

    /// <summary>
    /// 会話が2件以上のときに小さく添える「会話N件」の文。1件以下なら null(何も添えない)。
    /// </summary>
    /// <returns>「会話N件」または null。</returns>
    public string? BuildSessionCountText() => SessionCount >= 2 ? $"会話{SessionCount}件" : null;

    /// <summary>
    /// 設定からAIごとのキャラ名(カードに大きく出す名前)を取り出す。
    /// </summary>
    /// <param name="settings">アプリの設定。</param>
    /// <param name="engine">AI(Claude または Gemini)。</param>
    /// <returns>キャラ名(既定: Claude=クローディ、Gemini=ジェミリー)。</returns>
    public static string GetCharacterName(AppSettings settings, AgentEngineKind engine) => engine == AgentEngineKind.Gemini
        ? settings.EffectiveGeminiLeaderCharacterName
        : settings.EffectiveClaudeLeaderCharacterName;

    /// <summary>
    /// カードに小さく出すAIの名前(「Claude」/「Gemini」)を返す。
    /// </summary>
    /// <param name="engine">AI(Claude または Gemini)。</param>
    /// <returns>AIの名前。</returns>
    public static string GetEngineLabel(AgentEngineKind engine) => engine == AgentEngineKind.Gemini ? "Gemini" : "Claude";

    /// <summary>
    /// 同梱アバター画像のファイル名を返す。
    /// </summary>
    /// <param name="engine">AI(Claude または Gemini)。</param>
    /// <returns>同梱フォルダ内のファイル名。</returns>
    public static string GetAvatarFileName(AgentEngineKind engine) => engine == AgentEngineKind.Gemini
        ? GeminiAvatarFileName
        : ClaudeAvatarFileName;

    /// <summary>
    /// カードを出すAIを並び順(Claude→Gemini)で返す。CLIが使えるAIだけを出し、最近の会話が無くても出す(待機表示)。
    /// </summary>
    /// <param name="isClaudeCliAvailable">Claude CLIが使えるか。</param>
    /// <param name="isGeminiCliAvailable">Gemini CLIが使えるか。</param>
    /// <returns>カードを出すAIの一覧。</returns>
    public static IReadOnlyList<AgentEngineKind> GetVisibleEngines(bool isClaudeCliAvailable, bool isGeminiCliAvailable)
    {
        var engines = new List<AgentEngineKind>();
        if (isClaudeCliAvailable)
        {
            engines.Add(AgentEngineKind.Claude);
        }

        if (isGeminiCliAvailable)
        {
            engines.Add(AgentEngineKind.Gemini);
        }

        return engines;
    }
}
