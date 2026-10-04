using Xunit;

namespace AgentDeskApp.Tests;

public class AgentTemplateRepositoryTests
{
    [Fact]
    public void GetCategories_ReturnsExpectedCategories()
    {
        var categories = AgentTemplateRepository.GetCategories();

        Assert.NotEmpty(categories);
        Assert.Equal(12, categories.Count);
        Assert.Contains(categories, c => c.CategoryName.Contains("IT開発"));
        Assert.Contains(categories, c => c.CategoryName.Contains("動画制作"));
        Assert.Contains(categories, c => c.CategoryName.Contains("デザイン"));
        Assert.Contains(categories, c => c.CategoryName.Contains("教育・講師"));
        Assert.Contains(categories, c => c.CategoryName.Contains("マーケティング"));
        Assert.Contains(categories, c => c.CategoryName.Contains("カスタマーサポート"));
        Assert.DoesNotContain(categories, c => c.CategoryName.Contains("プロジェクトマネジメント"));
    }

    /// <summary>
    /// 共通メンバーと特別枠がカテゴリ一覧の先頭に、この順で並び、その後に職種カテゴリが続くことを確認する。
    /// </summary>
    [Fact]
    public void GetCategories_共通メンバーと特別枠が先頭に並ぶ()
    {
        var categories = AgentTemplateRepository.GetCategories();

        Assert.Contains("共通メンバー", categories[0].CategoryName);
        Assert.Contains("特別枠", categories[1].CategoryName);
        Assert.Contains("IT開発", categories[2].CategoryName);
        Assert.DoesNotContain(categories.Skip(2), c => c.CategoryName.Contains("共通メンバー") || c.CategoryName.Contains("特別枠"));
    }

    /// <summary>
    /// 共通メンバーはダンドリとキヅクの2人だけで、Id は旧PMカテゴリのまま変わらないことを確認する。
    /// </summary>
    [Fact]
    public void 共通メンバー_ダンドリとキヅクの2人で旧Idを維持する()
    {
        var common = AgentTemplateRepository.GetCategories().Single(c => c.CategoryName.Contains("共通メンバー"));

        Assert.Equal(new[] { "pm-progress", "pm-risk-detector" }, common.Templates.Select(t => t.Id).ToArray());
        Assert.Contains("ダンドリ", common.Templates[0].DisplayName);
        Assert.Contains("キヅク", common.Templates[1].DisplayName);
    }

    /// <summary>
    /// 廃止したPM4人（ワケル・クミアワセ・アツメ・ホウコク）がテンプレートに残っていないことを確認する。
    /// </summary>
    [Theory]
    [InlineData("pm-wbs")]
    [InlineData("pm-scheduler")]
    [InlineData("pm-collector")]
    [InlineData("pm-reporter")]
    public void 廃止したPMメンバーはテンプレートに含まれない(string id)
    {
        var templates = AgentTemplateRepository.GetCategories().SelectMany(c => c.Templates);

        Assert.DoesNotContain(templates, t => t.Id == id);
    }

    /// <summary>
    /// 特別枠3人の Id・呼び名・推奨モデルと、読むだけ（Read のみ）の決まりを確認する。
    /// </summary>
    /// <param name="id">エージェントId</param>
    /// <param name="callName">呼び名</param>
    /// <param name="model">推奨モデル</param>
    [Theory]
    [InlineData("premise-challenger", "キマグレ", "haiku")]
    [InlineData("sounding-board", "ホッコリ", "haiku")]
    [InlineData("breakthrough-analogist", "トッパ", "sonnet")]
    public void 特別枠_3人の定義(string id, string callName, string model)
    {
        var special = AgentTemplateRepository.GetCategories().Single(c => c.CategoryName.Contains("特別枠"));
        var template = special.Templates.Single(t => t.Id == id);

        Assert.Equal(3, special.Templates.Count);
        Assert.Equal(callName, AgentNameUniqueness.ExtractCallName(template.DisplayName));
        Assert.Equal(model, template.RecommendedModel);
        Assert.Equal("Read", template.Tools);
        Assert.Contains(callName, template.SystemPrompt);
        Assert.Contains("ファイルを変更しない", template.SystemPrompt);
    }

    /// <summary>
    /// Claude Code の既知ツール名。テンプレートの tools に書けるのはこの名前だけ。
    /// </summary>
    private static readonly string[] KnownToolNames =
    {
        "Read", "Edit", "Write", "Bash", "Grep", "Glob", "WebFetch", "WebSearch",
        "NotebookEdit", "TodoWrite", "Agent",
    };

    /// <summary>
    /// 全テンプレートの tools に旧名「Command」が含まれず、Claude Code の既知ツール名だけで構成されることを確認する。
    /// </summary>
    [Fact]
    public void 全テンプレートのToolsはClaudeCodeの既知ツール名だけで構成される()
    {
        var templates = AgentTemplateRepository.GetCategories().SelectMany(c => c.Templates).ToList();
        Assert.NotEmpty(templates);

        foreach (var t in templates)
        {
            var tools = t.Tools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.DoesNotContain("Command", tools);
            foreach (var tool in tools)
            {
                Assert.True(KnownToolNames.Contains(tool), $"{t.Id} の tools に未知のツール名 '{tool}' が含まれています");
            }
        }
    }

    [Fact]
    public void SelectedModel_DefaultsToRecommendedModel_ForOpusAgents()
    {
        var categories = AgentTemplateRepository.GetCategories();
        var allTemplates = categories.SelectMany(c => c.Templates).ToList();

        var kibishi = allTemplates.FirstOrDefault(t => t.Id == "design-critic");
        Assert.NotNull(kibishi);
        Assert.Equal("opus", kibishi.RecommendedModel);
        Assert.Equal("opus", kibishi.SelectedModel);

        var jiku = allTemplates.FirstOrDefault(t => t.Id == "marketing-concept");
        Assert.NotNull(jiku);
        Assert.Equal("opus", jiku.RecommendedModel);
        Assert.Equal("opus", jiku.SelectedModel);
    }

    [Fact]
    public void ItDevelopmentCategory_ContainsExpectedAgents()
    {
        var categories = AgentTemplateRepository.GetCategories();
        var itCat = categories.FirstOrDefault(c => c.CategoryName.Contains("IT開発"));

        Assert.NotNull(itCat);
        Assert.True(itCat.Templates.Count >= 8);

        // レビュー担当（シラベ）はOpus推奨
        var reviewer = itCat.Templates.FirstOrDefault(t => t.Id == "code-reviewer");
        Assert.NotNull(reviewer);
        Assert.Equal("opus", reviewer.RecommendedModel);
        Assert.Contains("シラベ", reviewer.DisplayName);

        // 実装担当（ツクル）はSonnet推奨
        var implementer = itCat.Templates.FirstOrDefault(t => t.Id == "software-engineer");
        Assert.NotNull(implementer);
        Assert.Equal("sonnet", implementer.RecommendedModel);
    }

    [Fact]
    public void AllTemplates_IdとDisplayNameの呼び名がテンプレート全体で重複しない()
    {
        var templates = AgentTemplateRepository.GetCategories().SelectMany(c => c.Templates).ToList();

        Assert.Empty(templates.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(templates
            .GroupBy(t => AgentNameUniqueness.ExtractCallName(t.DisplayName))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key));
    }

    [Theory]
    [InlineData("market-analyst", "シジョウ")]
    [InlineData("script-reviewer", "タタキ")]
    [InlineData("sales-researcher", "ウラドリ")]
    [InlineData("translation-critic", "ツウジル")]
    [InlineData("sales-faq-script", "カエシ")]
    [InlineData("cs-researcher", "ナレッジ")]
    public void 重複解消で改名したテンプレートの呼び名(string id, string expectedCallName)
    {
        var template = AgentTemplateRepository.GetCategories().SelectMany(c => c.Templates).Single(t => t.Id == id);

        Assert.Equal(expectedCallName, AgentNameUniqueness.ExtractCallName(template.DisplayName));
    }

    [Fact]
    public void ToClaudeMarkdown_GeneratesValidFrontmatter()
    {
        var item = new AgentTemplateItem
        {
            Id = "test-agent",
            DisplayName = "テスト担当",
            Description = "テスト用エージェント",
            SystemPrompt = "あなたはテスト担当です。",
            RecommendedModel = "opus",
            SelectedModel = "sonnet",
            Tools = "Read, Edit",
        };

        var md = item.ToClaudeMarkdown();

        Assert.StartsWith("---", md);
        Assert.Contains("name: test-agent", md);
        Assert.Contains("description: テスト用エージェント", md);
        Assert.Contains("displayName: テスト担当", md);
        Assert.Contains("model: sonnet", md);
        Assert.Contains("tools:", md);
        Assert.Contains("- Read", md);
        Assert.Contains("- Edit", md);
        Assert.Contains("あなたはテスト担当です。", md);
    }

    [Fact]
    public void ToClaudeMarkdown_WithoutDisplayName_OmitsDisplayNameLine()
    {
        var item = new AgentTemplateItem
        {
            Id = "test-agent-no-name",
            DisplayName = string.Empty,
            Description = "テスト用エージェント",
            SystemPrompt = "あなたはテスト担当です。",
            RecommendedModel = "sonnet",
            SelectedModel = "sonnet",
            Tools = "Read",
        };

        var md = item.ToClaudeMarkdown();

        Assert.DoesNotContain("displayName:", md);
    }
}
