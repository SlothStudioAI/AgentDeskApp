using AgentDeskApp;
using Xunit;

namespace AgentDeskApp.Tests;

/// <summary><see cref="OtherScopeMemberFinder"/>(追加先以外のスコープの同Idメンバー検出)のテスト。</summary>
public class OtherScopeMemberFinderTests
{
    private const string TargetDir = @"C:\studio\TeamA\.claude\agents";

    private static StudioAgentEntry Entry(string id, string label, string claudeDir)
    {
        var agent = new AgentDefinition(
            id, "desc", null, null, null, "body", AgentScope.Team, Path.Combine(claudeDir, $"{id}.md"), "表示名");
        return new StudioAgentEntry(agent, label, claudeDir);
    }

    [Fact]
    public void 追加先スコープ自身のメンバーは除外する()
    {
        var entries = new[] { Entry("qa", "TeamA", TargetDir) };

        Assert.Empty(OtherScopeMemberFinder.FindOtherScopeLabels("qa", TargetDir, entries));
    }

    [Fact]
    public void 他スコープに同じIdがいればその所属ラベルを返す()
    {
        var entries = new[]
        {
            Entry("qa", "TeamA", TargetDir),
            Entry("qa", "Studio / IT開発", @"C:\studio\IT\.claude\agents"),
        };

        var labels = OtherScopeMemberFinder.FindOtherScopeLabels("QA", TargetDir.ToUpperInvariant(), entries);

        Assert.Equal(["Studio / IT開発"], labels);
    }

    [Fact]
    public void 複数スコープにいれば全ラベルを重複なく返す()
    {
        var entries = new[]
        {
            Entry("qa", "全社", @"C:\global\.claude\agents"),
            Entry("qa", "Studio / IT開発", @"C:\studio\IT\.claude\agents"),
            Entry("qa", "Studio / IT開発", @"C:\studio\IT\.claude\agents"),
            Entry("qa", "TeamA", TargetDir),
        };

        var labels = OtherScopeMemberFinder.FindOtherScopeLabels("qa", TargetDir, entries);

        Assert.Equal(["全社", "Studio / IT開発"], labels);
        Assert.Equal("ℹ️他のチームにも登録済み: 全社、Studio / IT開発", OtherScopeMemberFinder.BuildBadgeText(labels));
    }

    [Fact]
    public void 該当なしなら空でバッジ文言も空()
    {
        var entries = new[] { Entry("other", "Studio / IT開発", @"C:\studio\IT\.claude\agents") };

        var labels = OtherScopeMemberFinder.FindOtherScopeLabels("qa", TargetDir, entries);

        Assert.Empty(labels);
        Assert.Equal(string.Empty, OtherScopeMemberFinder.BuildBadgeText(labels));
    }
}
