using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="DashboardActivityCollector"/>(Dashboard上部「稼働中・完了のメンバー」の選別ロジック)のテスト。
/// 完了カードのクリックで消える挙動は、既存の <see cref="MemberCardAttentionTracker"/> と組み合わせて確認する。
/// </summary>
public class DashboardActivityCollectorTests
{
    /// <summary>テスト用のメンバー(所属ラベル付き)を作る。</summary>
    private static StudioAgentEntry Entry(string id, string label = "全社", string? displayName = null) =>
        new(new AgentDefinition(id, "説明", null, null, null, "本文", AgentScope.Team, $"C:\\x\\{id}.md", displayName), label, "C:\\x");

    [Fact]
    public void Collect_作業中と完了未確認だけを対象にして待機中は除く()
    {
        var entries = new[] { Entry("running"), Entry("done"), Entry("idle"), Entry("cancelled"), Entry("timedout") };
        var states = new Dictionary<string, MemberActivityState>
        {
            ["running"] = MemberActivityState.Running,
            ["done"] = MemberActivityState.Done,
            ["idle"] = MemberActivityState.Idle,
            ["cancelled"] = MemberActivityState.Cancelled,
            ["timedout"] = MemberActivityState.TimedOut,
        };

        var result = DashboardActivityCollector.Collect(entries, id => states[id], id => id == "done");

        Assert.Equal(["done", "running"], result.Select(e => e.Agent.Name).ToArray());
    }

    [Fact]
    public void Collect_確認クリックで完了カードが消え作業中は消えない()
    {
        var tracker = new MemberCardAttentionTracker();
        var entries = new[] { Entry("a"), Entry("b") };
        var states = new Dictionary<string, MemberActivityState>();

        // aは作業中→完了、bは作業中のまま
        states["a"] = MemberActivityState.Running;
        states["b"] = MemberActivityState.Running;
        tracker.Observe("a", states["a"]);
        tracker.Observe("b", states["b"]);
        states["a"] = MemberActivityState.Done;
        tracker.Observe("a", states["a"]);

        var before = DashboardActivityCollector.Collect(entries, id => states[id], tracker.IsAwaitingAcknowledgement);
        Assert.Equal(2, before.Count);

        // 完了カードをクリックして確認すると消える。作業中のbは残る
        Assert.True(tracker.Acknowledge("a"));
        var after = DashboardActivityCollector.Collect(entries, id => states[id], tracker.IsAwaitingAcknowledgement);
        Assert.Equal(["b"], after.Select(e => e.Agent.Name).ToArray());

        // 確認済みのaが再び作業中になれば、また現れる
        states["a"] = MemberActivityState.Running;
        tracker.Observe("a", states["a"]);
        var again = DashboardActivityCollector.Collect(entries, id => states[id], tracker.IsAwaitingAcknowledgement);
        Assert.Equal(2, again.Count);
    }

    [Fact]
    public void Collect_同じIdは1人として数える()
    {
        var entries = new[] { Entry("dup", "全社"), Entry("dup", "WS / チームA"), Entry("other") };

        var result = DashboardActivityCollector.Collect(entries, _ => MemberActivityState.Running, _ => false);

        Assert.Equal(2, result.Count);
        Assert.Single(result, e => e.Agent.Name == "dup");
        Assert.Equal("全社", result.Single(e => e.Agent.Name == "dup").ScopeLabel);
    }

    [Fact]
    public void Collect_0件のとき空になり空表示を出す()
    {
        var result = DashboardActivityCollector.Collect([Entry("x")], _ => MemberActivityState.Idle, _ => false);

        Assert.Empty(result);
        Assert.True(DashboardActivityCollector.ShouldShowEmptyMessage(result.Count));
        Assert.False(DashboardActivityCollector.ShouldShowEmptyMessage(1));
    }

    [Fact]
    public void IsVisible_完了は未確認のときだけ出す()
    {
        Assert.True(DashboardActivityCollector.IsVisible(MemberActivityState.Running, false));
        Assert.True(DashboardActivityCollector.IsVisible(MemberActivityState.Done, true));
        Assert.False(DashboardActivityCollector.IsVisible(MemberActivityState.Done, false));
        Assert.False(DashboardActivityCollector.IsVisible(MemberActivityState.Idle, true));
    }

    [Theory]
    [InlineData("Sloth Studio / AgentDeskApp", "AgentDeskApp")]
    [InlineData("Sloth Studio", "Sloth Studio")]
    [InlineData("全社", "全社")]
    public void FormatTeamLabel_チーム名だけを取り出す(string label, string expected)
    {
        Assert.Equal(expected, DashboardActivityCollector.FormatTeamLabel(label));
    }
}
