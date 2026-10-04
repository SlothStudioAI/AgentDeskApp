using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="ScopeRuleLabelFormatter"/>(ルールボタン文言の組み立て)に関するテスト。
/// </summary>
public class ScopeRuleLabelFormatterTests
{
    [Fact]
    public void Format_ワークスペースは名前にワークスペースのルールが付く()
    {
        Assert.Equal("Sloth Studio ワークスペースのルール", ScopeRuleLabelFormatter.Format(ScopeRuleKind.Group, "Sloth Studio"));
    }

    [Fact]
    public void Format_チームは名前にチームのルールが付く()
    {
        Assert.Equal("IT開発 チームのルール", ScopeRuleLabelFormatter.Format(ScopeRuleKind.Team, "IT開発"));
    }

    [Fact]
    public void Format_Dashboardは名前があってもルールのみ()
    {
        Assert.Equal("ルール", ScopeRuleLabelFormatter.Format(ScopeRuleKind.Department, "All"));
    }

    [Fact]
    public void Format_長い名前もそのまま連結される()
    {
        var name = new string('あ', 60);
        Assert.Equal(name + " チームのルール", ScopeRuleLabelFormatter.Format(ScopeRuleKind.Team, name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_空名前はルールのみ(string? name)
    {
        Assert.Equal("ルール", ScopeRuleLabelFormatter.Format(ScopeRuleKind.Group, name));
        Assert.Equal("ルール", ScopeRuleLabelFormatter.Format(ScopeRuleKind.Team, name));
    }
}
