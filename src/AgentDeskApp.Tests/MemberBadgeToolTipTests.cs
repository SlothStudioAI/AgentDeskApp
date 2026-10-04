using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// メンバーカードのエンジンバッジのツールチップ文言(<see cref="MemberBadgeToolTip"/>)のテスト。
/// </summary>
public class MemberBadgeToolTipTests
{
    /// <summary>エンジン種別ごとに期待する文言が返ること。</summary>
    [Theory]
    [InlineData(AgentEngineKind.Claude, "Claude に配備されています")]
    [InlineData(AgentEngineKind.Gemini, "Gemini に配備されています")]
    [InlineData(AgentEngineKind.Shared, "Claude と Gemini の両方に配備されています")]
    public void For_エンジン種別に応じた文言を返す(AgentEngineKind engine, string expected)
    {
        Assert.Equal(expected, MemberBadgeToolTip.For(engine));
    }
}
