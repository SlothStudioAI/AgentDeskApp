using Xunit;

namespace AgentDeskApp.Tests;

/// <summary>編集画面の配備先別 表示可否判定のテスト。</summary>
public class EditFieldVisibilityTests
{
    [Fact]
    public void Claude専用はClaudeモデルと色のみ表示()
    {
        Assert.Equal(new EditFieldVisibility(true, true, false), EditFieldVisibility.For(AgentEngineKind.Claude));
    }

    [Fact]
    public void Gemini専用はGeminiモデルのみ表示()
    {
        Assert.Equal(new EditFieldVisibility(false, false, true), EditFieldVisibility.For(AgentEngineKind.Gemini));
    }

    [Fact]
    public void Sharedはすべて表示()
    {
        Assert.Equal(new EditFieldVisibility(true, true, true), EditFieldVisibility.For(AgentEngineKind.Shared));
    }
}
