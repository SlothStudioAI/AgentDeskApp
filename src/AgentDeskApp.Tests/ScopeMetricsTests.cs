using Xunit;

namespace AgentDeskApp.Tests;

/// <summary>
/// BUG-24: TEAM欄(WORKSPACE表示)の数字の集計(<see cref="ScopeMetrics"/>)のテスト。
/// グループ選択時に直下だけでなく配下の全チームを含めて数えること、同じIdを二重に数えないこと、
/// リーダーをAIごとに1人で数えることを確認する。
/// </summary>
public class ScopeMetricsTests
{
    private static readonly IReadOnlyDictionary<string, MemberActivityState> NoStates =
        new Dictionary<string, MemberActivityState>();

    /// <summary>テスト用のメンバー定義を作る。</summary>
    /// <param name="name">Id(frontmatterのname)。</param>
    /// <param name="engine">対応AI。</param>
    /// <param name="folder">置き場所(チーム名など)。</param>
    private static AgentDefinition Agent(string name, AgentEngineKind engine = AgentEngineKind.Claude, string folder = "group") =>
        new(name, "desc", null, null, null, "body", AgentScope.Team, $@"C:\studio\{folder}\.claude\agents\{name}.md", Engine: engine);

    /// <summary>テスト用のリーダーの会話を作る。</summary>
    /// <param name="id">セッションID。</param>
    /// <param name="state">状態。</param>
    /// <param name="engine">AI。</param>
    private static LeaderSessionInfo Leader(string id, MemberActivityState state, AgentEngineKind engine) =>
        new(id, state, null, engine);

    [Fact]
    public void 直下のみなら直下の人数と内訳を数える()
    {
        IReadOnlyList<AgentDefinition> direct =
        [
            Agent("dandori"), Agent("kiduku"), Agent("kimagure", AgentEngineKind.Gemini), Agent("hokkori", AgentEngineKind.Shared),
        ];

        var metrics = ScopeMetrics.Calculate([direct], NoStates, []);

        Assert.Equal(4, metrics.TotalMembers);
        Assert.Equal(3, metrics.ClaudeMembers);
        Assert.Equal(2, metrics.GeminiMembers);
        Assert.Equal(0, metrics.TotalActive);
    }

    [Fact]
    public void 配下のチームも含めた合計を数える()
    {
        IReadOnlyList<AgentDefinition> direct = [Agent("dandori"), Agent("kiduku")];
        IReadOnlyList<AgentDefinition> it = [Agent("tsukuru", folder: "it"), Agent("ijiwaru", AgentEngineKind.Shared, "it")];
        IReadOnlyList<AgentDefinition> video = [Agent("egaku", AgentEngineKind.Gemini, "video")];

        var metrics = ScopeMetrics.Calculate([direct, it, video], NoStates, []);

        Assert.Equal(5, metrics.TotalMembers);
        Assert.Equal(4, metrics.ClaudeMembers);
        Assert.Equal(2, metrics.GeminiMembers);
    }

    [Fact]
    public void 同じIdが複数の場所にいても1人として数える()
    {
        IReadOnlyList<AgentDefinition> direct = [Agent("dandori"), Agent("Tsukuru")];
        IReadOnlyList<AgentDefinition> it = [Agent("tsukuru", AgentEngineKind.Gemini, "it")];

        var metrics = ScopeMetrics.Calculate([direct, it], NoStates, []);

        // tsukuru は大文字小文字違いでも同じId → 1人。Claude側とGemini側にいるので両方の内訳に入る
        Assert.Equal(2, metrics.TotalMembers);
        Assert.Equal(2, metrics.ClaudeMembers);
        Assert.Equal(1, metrics.GeminiMembers);
    }

    [Fact]
    public void 作業中は配下のチームも含めメンバーとリーダーを合計する()
    {
        IReadOnlyList<AgentDefinition> direct = [Agent("dandori"), Agent("kiduku")];
        IReadOnlyList<AgentDefinition> it = [Agent("tsukuru", folder: "it"), Agent("ijiwaru", folder: "it")];
        IReadOnlyList<AgentDefinition> cs = [Agent("tsukuru", folder: "cs")];
        var states = new Dictionary<string, MemberActivityState>
        {
            ["dandori"] = MemberActivityState.Running,
            ["tsukuru"] = MemberActivityState.Running,
            ["ijiwaru"] = MemberActivityState.Done,
            ["outside"] = MemberActivityState.Running, // 表示範囲外のメンバーは数えない
        };
        LeaderSessionInfo[] leaders =
        [
            Leader("c1", MemberActivityState.Running, AgentEngineKind.Claude),
            Leader("c2", MemberActivityState.Running, AgentEngineKind.Claude),
            Leader("g1", MemberActivityState.Done, AgentEngineKind.Gemini),
        ];

        var metrics = ScopeMetrics.Calculate([direct, it, cs], states, leaders);

        // メンバー: dandori + tsukuru(2か所にいても1人) = 2、リーダー: Claudeの会話2件でも1人
        Assert.Equal(2, metrics.RunningMembers);
        Assert.Equal(1, metrics.ActiveLeaders);
        Assert.Equal(3, metrics.TotalActive);
    }

    [Fact]
    public void リーダーはAIごとに1人で数える()
    {
        LeaderSessionInfo[] leaders =
        [
            Leader("c1", MemberActivityState.Running, AgentEngineKind.Claude),
            Leader("c2", MemberActivityState.Running, AgentEngineKind.Claude),
            Leader("g1", MemberActivityState.Running, AgentEngineKind.Gemini),
            Leader("g2", MemberActivityState.Done, AgentEngineKind.Gemini),
        ];

        Assert.Equal(2, ScopeMetrics.CountActiveLeaders(leaders));
        Assert.Equal(0, ScopeMetrics.CountActiveLeaders([Leader("c1", MemberActivityState.Done, AgentEngineKind.Claude)]));
    }

    [Fact]
    public void メンバーがいなければすべて0()
    {
        var metrics = ScopeMetrics.Calculate([[], []], NoStates, []);

        Assert.Equal(0, metrics.TotalMembers);
        Assert.Equal(0, metrics.ClaudeMembers);
        Assert.Equal(0, metrics.GeminiMembers);
        Assert.Equal(0, metrics.TotalActive);
    }
}
