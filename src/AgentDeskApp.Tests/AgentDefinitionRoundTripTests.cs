using System.IO;
using System.Runtime.CompilerServices;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// エージェント定義の書き出し(Save / SaveGeminiSkill / AgentDeployWriter)→読み込み(Load)の往復テスト。
/// 特殊文字入りの値、全テンプレートの3配置先、従来形式(tools が YAML 箇条書き)の実ファイルを検証する。一時ディレクトリを使う。
/// </summary>
public sealed class AgentDefinitionRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AgentRoundTrip_" + Guid.NewGuid().ToString("N"));
    private string ClaudeDir => Path.Combine(_root, ".claude", "agents");
    private string SkillsDir => Path.Combine(_root, ".agents", "skills");

    public AgentDefinitionRoundTripTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>往復で壊れてはいけない特殊文字入りの説明文の一覧。</summary>
    public static TheoryData<string> SpecialTexts() => new()
    {
        "設計に\"沿って\"実装する",
        "\"全体が引用符\"",
        "'シングル'クォート入り",
        "'全体がシングル'",
        "目的: 実装する",
        "末尾コロン:",
        "コメント # ではない",
        "# 先頭シャープ",
        "- 先頭ハイフン",
        "[先頭の角括弧] 説明",
        "{先頭の波括弧}",
        "「日本語括弧」と（全角括弧）と【隅付き】",
        "*アスタリスク始まり & アンパサンド",
        "C:\\Users\\someone\\path と \\n の文字列",
        "true",
        "123",
        "%パーセント @アット `バッククォート",
        "---",
        "長い文章です。" + string.Concat(Enumerable.Repeat("これは非常に長い説明文で、委譲判断に使われます。\"引用\": コロン # 記号。", 30)),
    };

    [Theory]
    [MemberData(nameof(SpecialTexts))]
    public void Claude_特殊文字入りの値が往復で保持される(string text)
    {
        var file = Path.Combine(ClaudeDir, "agent-x.md");
        AgentDefinitionLoader.Save(file, text.Length < 40 ? text : "agent-x", text, "Read, Edit", "sonnet", "#3B82F6", "本文", displayName: text);

        var loaded = AgentDefinitionLoader.ParseFile(file, AgentScope.Global);
        Assert.Equal(text, loaded.Description);
        Assert.Equal(text, loaded.DisplayName);
        Assert.Equal("Read, Edit", loaded.Tools);
        Assert.Equal("sonnet", loaded.Model);
        Assert.Equal("#3B82F6", loaded.Color);
        Assert.Equal("本文", loaded.Body);
    }

    [Theory]
    [MemberData(nameof(SpecialTexts))]
    public void Gemini_特殊文字入りの値が往復で保持される(string text)
    {
        var dir = Path.Combine(SkillsDir, "agent-x");
        var file = AgentDefinitionLoader.SaveGeminiSkill(dir, "agent-x", text, "本文", displayName: text, color: "#3B82F6", model: "flash");

        var loaded = AgentDefinitionLoader.ParseSkillFile(file, AgentScope.Global);
        Assert.Equal(text, loaded.Description);
        Assert.Equal(text, loaded.DisplayName);
        Assert.Equal("flash", loaded.Model);
        Assert.Equal("#3B82F6", loaded.Color);
    }

    [Fact]
    public void Claude_改行入りの説明文は1行のfrontmatterに書かれ往復で保持される()
    {
        var file = Path.Combine(ClaudeDir, "agent-x.md");
        var text = "1行目\n2行目: コロン\r\n3行目\n---\nname: 偽";
        AgentDefinitionLoader.Save(file, "agent-x", text, null, null, null, "本文");

        var raw = File.ReadAllText(file);
        // frontmatterの区切り(---)は先頭と終端の2つだけで、説明文が余計なキーや区切りを作らない
        Assert.Equal(2, raw.Split('\n').Count(l => l.Trim() == "---"));
        var loaded = AgentDefinitionLoader.ParseFile(file, AgentScope.Global);
        Assert.Equal("1行目\n2行目: コロン\n3行目\n---\nname: 偽", loaded.Description);
        Assert.Equal("agent-x", loaded.Name);
    }

    [Fact]
    public void Gemini_改行入りの説明文は空白に畳まれ区切りを壊さない()
    {
        var dir = Path.Combine(SkillsDir, "agent-x");
        var file = AgentDefinitionLoader.SaveGeminiSkill(dir, "agent-x", "1行目\n2行目: コロン\r\n3行目", "本文");

        var loaded = AgentDefinitionLoader.ParseSkillFile(file, AgentScope.Global);
        Assert.Equal("1行目 2行目: コロン 3行目", loaded.Description);
    }

    [Fact]
    public void Claude_本文に区切りや特殊文字があっても往復で保持される()
    {
        var file = Path.Combine(ClaudeDir, "agent-x.md");
        var body = "# 見出し\n\n- 箇条書き: あ\n\n```yaml\n---\nname: x\n---\n```\n\n\"引用\" # コメント";
        AgentDefinitionLoader.Save(file, "agent-x", "説明", null, null, null, body);

        Assert.Equal(body, AgentDefinitionLoader.ParseFile(file, AgentScope.Global).Body);
    }

    [Fact]
    public void FormatYamlScalar_安全な値はそのまま返す()
    {
        Assert.Equal("Read, Edit, Grep", AgentDefinitionLoader.FormatYamlScalar("Read, Edit, Grep"));
        Assert.Equal("実装担当 (ツクル)", AgentDefinitionLoader.FormatYamlScalar("実装担当 (ツクル)"));
        Assert.Equal("\"#3B82F6\"", AgentDefinitionLoader.FormatYamlScalar("#3B82F6"));
    }

    /// <summary>全職種テンプレートを Claude / Gemini / Shared の各配置先に書き出し、読み込んで内容が保持されることを確認する。</summary>
    [Theory]
    [InlineData(AgentEngineKind.Claude)]
    [InlineData(AgentEngineKind.Gemini)]
    [InlineData(AgentEngineKind.Shared)]
    public void 全テンプレートが配置先ごとに書き出し読み込みで保持される(AgentEngineKind engine)
    {
        var items = AgentTemplateRepository.GetCategories().SelectMany(c => c.Templates).ToList();
        Assert.NotEmpty(items);

        foreach (var t in items)
        {
            var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, t.Id, engine);
            AgentDeployWriter.Write(
                plan, ClaudeDir, SkillsDir, t.Id, t.Description, t.Tools, t.SelectedModel,
                t.SelectedGeminiModel, t.AvatarColor, t.SystemPrompt, t.DisplayName, null, false);
        }

        var loaded = AgentDefinitionLoader.LoadScopeAgents(ClaudeDir, SkillsDir, AgentScope.Global).ToDictionary(a => a.Name);
        Assert.Equal(items.Select(t => t.Id).Distinct().Count(), loaded.Count);

        foreach (var t in items)
        {
            var a = loaded[t.Id];
            Assert.Equal(engine, a.Engine);
            Assert.Equal(t.Description, a.Description);
            Assert.Equal(t.DisplayName, a.DisplayName);
            Assert.Equal(t.AvatarColor, a.Color);

            if (engine != AgentEngineKind.Gemini)
            {
                Assert.Equal(t.Tools, a.Tools);
                Assert.Equal(t.SelectedModel, a.Model);
                // 改行コード(CRLF/LF)の違いは比較対象外にする(ソースがCRLFだとテンプレート本文もCRLFになるため)
                Assert.Equal(t.SystemPrompt.Replace("\r\n", "\n").Trim(), a.Body);
            }

            if (engine == AgentEngineKind.Gemini)
            {
                Assert.Equal(t.SelectedGeminiModel, a.Model);
            }
            else if (engine == AgentEngineKind.Shared)
            {
                Assert.Equal(t.SelectedGeminiModel, a.GeminiModel);
            }
        }
    }

    /// <summary>リポジトリの実ファイル(.claude/agents、tools が YAML 箇条書き)をコピー上で読み込み・再保存しても内容が変わらないことを確認する。</summary>
    [Fact]
    public void 従来形式の実ファイルを読み込み再保存しても内容が保持される()
    {
        var source = FindRepoClaudeAgentsDir();
        if (source is null)
        {
            return; // リポジトリ外で実行された場合は対象なし
        }

        var copyDir = Path.Combine(_root, "copy");
        Directory.CreateDirectory(copyDir);
        foreach (var f in Directory.EnumerateFiles(source, "*.md"))
        {
            File.Copy(f, Path.Combine(copyDir, Path.GetFileName(f)));
        }

        var before = AgentDefinitionLoader.LoadDirectory(copyDir, AgentScope.Global);
        Assert.NotEmpty(before);
        Assert.Equal(Directory.EnumerateFiles(copyDir, "*.md").Count(), before.Count);

        foreach (var d in before)
        {
            AgentDefinitionLoader.Save(d.FilePath, d.Name, d.Description, d.Tools, d.Model, d.Color, d.Body, d.DisplayName,
                avatar: null);
        }

        var after = AgentDefinitionLoader.LoadDirectory(copyDir, AgentScope.Global).ToDictionary(a => a.Name);
        foreach (var b in before)
        {
            var a = after[b.Name];
            Assert.Equal(b.Description, a.Description);
            Assert.Equal(b.Tools, a.Tools);
            Assert.Equal(b.Model, a.Model);
            Assert.Equal(b.Color, a.Color);
            Assert.Equal(b.DisplayName, a.DisplayName);
            Assert.Equal(b.Body, a.Body);
        }
    }

    /// <summary>ソースファイルの位置から親をたどり、.claude/agents フォルダを探す(無ければnull)。</summary>
    /// <param name="thisFile">このソースファイルのパス(自動設定)。</param>
    private static string? FindRepoClaudeAgentsDir([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, ".claude", "agents");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
