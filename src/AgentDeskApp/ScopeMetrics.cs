namespace AgentDeskApp;

/// <summary>
/// TEAM欄(WORKSPACE/TEAM表示)の上部に出す数字の集計結果(BUG-24)。
/// </summary>
/// <param name="TotalMembers">専門メンバー数(同じIdは1人として数える)。</param>
/// <param name="ClaudeMembers">Claudeで動くメンバー数(Claude専用＋両対応)。</param>
/// <param name="GeminiMembers">Geminiで動くメンバー数(Gemini専用＋両対応)。</param>
/// <param name="RunningMembers">作業中のメンバー数(同じIdは1人として数える)。</param>
/// <param name="ActiveLeaders">作業中のリーダー数(AIごとに1人。会話の数ではない)。</param>
public sealed record ScopeMetrics(
    int TotalMembers,
    int ClaudeMembers,
    int GeminiMembers,
    int RunningMembers,
    int ActiveLeaders)
{
    /// <summary>作業中の合計(メンバー＋リーダー)。「N稼働中」バッジの数字にも使う。</summary>
    public int TotalActive => RunningMembers + ActiveLeaders;

    /// <summary>
    /// 表示範囲のメンバー・状態・リーダーの会話から、TEAM欄の数字をまとめて計算する。
    /// グループ(ワークスペース)選択時は「グループ直下＋配下の全チーム」の一覧を、
    /// チーム・全社選択時はその範囲1つ分の一覧を <paramref name="memberLists"/> に渡す
    /// (=メンバー欄に表示しているカードと同じ範囲で数える)。
    /// 同じId(frontmatterのname、大文字小文字は区別しない)が複数の場所にいても1人として数える。
    /// これは同じフォルダ内でClaude側とGemini側の同名を1人(両対応)にまとめる既存の扱いと、
    /// 作業状態がIdごとに1つしか持てないこと(同名カードは一緒に光る)に合わせたもの。
    /// 複数の場所に同じIdがいる場合、どこかがClaude対応ならClaude、どこかがGemini対応ならGeminiに数える。
    /// </summary>
    /// <param name="memberLists">範囲ごと(直下・各チーム)のメンバー一覧。</param>
    /// <param name="states">メンバーId(name)ごとの現在の状態。</param>
    /// <param name="leaderSessions">表示範囲のリーダーの会話一覧。</param>
    /// <returns>集計結果。</returns>
    public static ScopeMetrics Calculate(
        IEnumerable<IReadOnlyList<AgentDefinition>> memberLists,
        IReadOnlyDictionary<string, MemberActivityState> states,
        IEnumerable<LeaderSessionInfo> leaderSessions)
    {
        // Idごとにまとめる(大文字小文字は区別しない。LoadScopeAgentsのClaude/Geminiマージと同じ比較)
        var byId = memberLists
            .SelectMany(list => list)
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var claude = byId.Count(g => g.Any(a => a.Engine is AgentEngineKind.Claude or AgentEngineKind.Shared));
        var gemini = byId.Count(g => g.Any(a => a.Engine is AgentEngineKind.Gemini or AgentEngineKind.Shared));
        var running = byId.Count(g => g.Any(a =>
            states.TryGetValue(a.Name, out var state) && state == MemberActivityState.Running));

        return new ScopeMetrics(byId.Count, claude, gemini, running, CountActiveLeaders(leaderSessions));
    }

    /// <summary>
    /// 作業中のリーダー数を数える。リーダーカードと同じく「AIごとに1人」で、
    /// 同じAIの会話がいくつ作業中でも1人として数える。
    /// </summary>
    /// <param name="leaderSessions">リーダーの会話一覧。</param>
    /// <returns>作業中の会話を1つ以上持つAIの数。</returns>
    public static int CountActiveLeaders(IEnumerable<LeaderSessionInfo> leaderSessions) => leaderSessions
        .Where(s => s.State == MemberActivityState.Running)
        .Select(s => s.Engine)
        .Distinct()
        .Count();
}
