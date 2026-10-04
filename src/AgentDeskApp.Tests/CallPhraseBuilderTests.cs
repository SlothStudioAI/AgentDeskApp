using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="CallPhraseBuilder"/>(「呼びかけ文をコピー」の文の組み立て)に関するテスト。
/// </summary>
public class CallPhraseBuilderTests
{
    [Fact]
    public void Build_表示名があれば正式名と表示名を入れた既定の文になる()
    {
        var phrase = CallPhraseBuilder.Build(
            "software-engineer", "ツクル",
            AppSettings.DefaultCallPhraseTemplate, AppSettings.DefaultCallPhraseTemplateWithoutDisplayName);

        Assert.Equal("software-engineer エージェント（ツクル）に、次の作業を頼んでください：", phrase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("software-engineer")]
    public void Build_表示名が無いか正式名と同じならnameだけの文になる(string? displayName)
    {
        var phrase = CallPhraseBuilder.Build(
            "software-engineer", displayName,
            AppSettings.DefaultCallPhraseTemplate, AppSettings.DefaultCallPhraseTemplateWithoutDisplayName);

        Assert.Equal("software-engineer エージェントに、次の作業を頼んでください：", phrase);
    }

    [Fact]
    public void Build_設定のひな形を使って置換できる()
    {
        var phrase = CallPhraseBuilder.Build(
            "researcher", "シラベ",
            "@{name}（{displayName}）さん、お願いします：", "@{name} さん、お願いします：");

        Assert.Equal("@researcher（シラベ）さん、お願いします：", phrase);
    }

    [Fact]
    public void Build_表示名なし用ひな形にdisplayNameがあっても正式名で埋めて壊れない()
    {
        var phrase = CallPhraseBuilder.Build("researcher", null, "unused", "{name}（{displayName}）へ：");

        Assert.Equal("researcher（researcher）へ：", phrase);
    }

    [Fact]
    public void Build_前後の空白は取り除く()
    {
        var phrase = CallPhraseBuilder.Build(
            " researcher ", " シラベ ",
            AppSettings.DefaultCallPhraseTemplate, AppSettings.DefaultCallPhraseTemplateWithoutDisplayName);

        Assert.Equal("researcher エージェント（シラベ）に、次の作業を頼んでください：", phrase);
    }

    [Fact]
    public void Build_エージェント定義と設定から組み立てられる()
    {
        var agent = new AgentDefinition(
            "software-engineer", "説明", null, null, null, string.Empty, AgentScope.Global, @"C:\dummy.md", DisplayName: "ツクル");

        var phrase = CallPhraseBuilder.Build(agent, AppSettings.Default);

        Assert.Equal("software-engineer エージェント（ツクル）に、次の作業を頼んでください：", phrase);
    }

    [Fact]
    public void Build_設定のひな形が空欄なら既定のひな形を使う()
    {
        var agent = new AgentDefinition(
            "software-engineer", "説明", null, null, null, string.Empty, AgentScope.Global, @"C:\dummy.md", DisplayName: "ツクル");
        var settings = AppSettings.Default with { CallPhraseTemplate = "", CallPhraseTemplateWithoutDisplayName = null };

        var phrase = CallPhraseBuilder.Build(agent, settings);

        Assert.Equal("software-engineer エージェント（ツクル）に、次の作業を頼んでください：", phrase);
    }
}
