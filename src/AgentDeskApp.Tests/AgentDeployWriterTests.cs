using System.IO;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="AgentDeployWriter"/>(配置先に応じた書き出し先の決定・書き出し)のテスト。一時ディレクトリを使う。
/// </summary>
public sealed class AgentDeployWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AgentDeployWriterTests_" + Guid.NewGuid().ToString("N"));
    private string ClaudeDir => Path.Combine(_root, ".claude", "agents");
    private string SkillsDir => Path.Combine(_root, ".agents", "skills");

    public AgentDeployWriterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WriteAll(AgentDeployPlan plan, string id = "helper", string? gemini = "flash", bool remove = false) =>
        AgentDeployWriter.Write(plan, ClaudeDir, SkillsDir, id, "説明", "Read, Edit", "sonnet", gemini, null, "本文", "ヘルパー", null, remove);

    [Fact]
    public void Plan_Claudeはmdファイルだけを出力先にする()
    {
        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Claude);
        Assert.Equal(Path.Combine(ClaudeDir, "helper.md"), plan.ClaudeFilePath);
        Assert.Null(plan.GeminiSkillDir);
        Assert.Null(plan.GeminiSkillFilePath);
        Assert.False(plan.HasExisting);
    }

    [Fact]
    public void Plan_GeminiはSKILLmdだけを出力先にする()
    {
        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Gemini);
        Assert.Null(plan.ClaudeFilePath);
        Assert.Equal(Path.Combine(SkillsDir, "helper", "SKILL.md"), plan.GeminiSkillFilePath);
    }

    [Fact]
    public void Plan_Sharedは両方を出力先にする()
    {
        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Shared);
        Assert.NotNull(plan.ClaudeFilePath);
        Assert.NotNull(plan.GeminiSkillDir);
    }

    [Fact]
    public void Plan_既存ファイルがあればExistingPathsに入る()
    {
        Directory.CreateDirectory(ClaudeDir);
        File.WriteAllText(Path.Combine(ClaudeDir, "helper.md"), "x");
        Directory.CreateDirectory(Path.Combine(SkillsDir, "helper"));

        Assert.Single(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Claude).ExistingPaths);
        Assert.Single(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Gemini).ExistingPaths);
        Assert.Equal(2, AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Shared).ExistingPaths.Count);
        Assert.False(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "other", AgentEngineKind.Shared).HasExisting);
    }

    [Fact]
    public void Write_Claudeは配置先外のGeminiを作らない()
    {
        WriteAll(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Claude));
        Assert.True(File.Exists(Path.Combine(ClaudeDir, "helper.md")));
        Assert.False(Directory.Exists(Path.Combine(SkillsDir, "helper")));
    }

    [Fact]
    public void Write_Geminiはモデルを書き読み込むとGemini種別になる()
    {
        WriteAll(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Gemini), gemini: "pro");
        var skill = Path.Combine(SkillsDir, "helper", "SKILL.md");
        Assert.False(File.Exists(Path.Combine(ClaudeDir, "helper.md")));
        Assert.Contains("model: pro", File.ReadAllText(skill));
    }

    [Fact]
    public void Write_Sharedは両方を書き出す()
    {
        WriteAll(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Shared));
        Assert.True(File.Exists(Path.Combine(ClaudeDir, "helper.md")));
        Assert.True(File.Exists(Path.Combine(SkillsDir, "helper", "SKILL.md")));
    }

    [Fact]
    public void Write_removeUndeployedSideなら配置先外の既存定義を整理する()
    {
        WriteAll(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Shared));
        WriteAll(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Claude), remove: true);
        Assert.True(File.Exists(Path.Combine(ClaudeDir, "helper.md")));
        Assert.False(Directory.Exists(Path.Combine(SkillsDir, "helper")));
    }

    [Fact]
    public void Write_removeUndeployedSideがfalseなら配置先外の既存定義を残す()
    {
        WriteAll(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Shared));
        WriteAll(AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Claude), remove: false);
        Assert.True(Directory.Exists(Path.Combine(SkillsDir, "helper")));
    }

    [Theory]
    [InlineData(ModelTier.Inherit, "継承", "inherit", "inherit")]
    [InlineData(ModelTier.UltraLight, "超軽量・最速", "haiku", "flash_lite")]
    [InlineData(ModelTier.Standard, "高速・低コスト(標準バランス)", "sonnet", "flash")]
    [InlineData(ModelTier.Highest, "最高知能", "opus", "pro")]
    public void ModelTierMap_段階から両エンジンのモデルとラベルを返す(ModelTier tier, string label, string claude, string gemini)
    {
        Assert.Equal(label, ModelTierMap.Label(tier));
        Assert.Equal(claude, ModelTierMap.ClaudeModel(tier));
        Assert.Equal(gemini, ModelTierMap.GeminiModel(tier));
        Assert.Equal(tier, ModelTierMap.FromLabel(label));
    }

    [Theory]
    [InlineData("opus", ModelTier.Highest)]
    [InlineData("sonnet", ModelTier.Standard)]
    [InlineData("haiku", ModelTier.UltraLight)]
    [InlineData("inherit", ModelTier.Inherit)]
    [InlineData("unknown", ModelTier.Standard)]
    [InlineData(null, ModelTier.Standard)]
    public void ModelTierMap_Claudeモデルから推奨段階を返す(string? claude, ModelTier expected) =>
        Assert.Equal(expected, ModelTierMap.FromClaudeModel(claude));

    [Fact]
    public void Write_Sharedは段階のClaudeモデルとGeminiモデルをそれぞれ書く()
    {
        var tier = ModelTier.Highest;
        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "tiered", AgentEngineKind.Shared);
        AgentDeployWriter.Write(
            plan, ClaudeDir, SkillsDir, "tiered", "d", "Read",
            ModelTierMap.ClaudeModel(tier), ModelTierMap.GeminiModel(tier), null, "body", "名前", null, false);

        var loaded = AgentDefinitionLoader.LoadScopeAgents(ClaudeDir, SkillsDir, AgentScope.Global).Single(a => a.Name == "tiered");
        Assert.Equal("opus", loaded.Model);
        Assert.Equal("pro", loaded.GeminiModel);
    }

    [Fact]
    public void CopyBundledAvatarIfExists_配置先ごとにコピーし既存は上書きしない()
    {
        var avatars = Path.Combine(_root, "avatars");
        Directory.CreateDirectory(avatars);
        File.WriteAllText(Path.Combine(avatars, "helper.png"), "new");

        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Shared);
        Directory.CreateDirectory(ClaudeDir);
        File.WriteAllText(Path.Combine(ClaudeDir, "helper.png"), "old");
        AgentDeployWriter.CopyBundledAvatarIfExists(plan, ClaudeDir, "helper", avatars);

        Assert.Equal("old", File.ReadAllText(Path.Combine(ClaudeDir, "helper.png")));
        Assert.True(File.Exists(Path.Combine(SkillsDir, "helper", "helper.png")));
        Assert.True(File.Exists(Path.Combine(SkillsDir, "helper", "avatar.png")));
    }

    // ---- 配備先変更時のアバター保持(回帰テスト) ----

    /// <summary>同梱画像フォルダ(helper.jpg入り)を一時ディレクトリに作る。</summary>
    private string MakeBundledDir()
    {
        var dir = Path.Combine(_root, "bundled");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "helper.jpg"), [1, 2, 3]);
        return dir;
    }

    /// <summary>指定の配置先で保存し、その配置先から読み直した定義のアバターパスを返す(同梱画像フォルダは差し替え)。</summary>
    private string? SaveAndLoadAvatar(AgentEngineKind engine, string? currentAvatar, string bundledDir, string? avatarField = null)
    {
        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", engine);
        if (currentAvatar is not null)
        {
            AgentDeployWriter.CopyAvatarToTargets(plan, ClaudeDir, "helper", currentAvatar);
        }

        AgentDeployWriter.Write(plan, ClaudeDir, SkillsDir, "helper", "説明", "Read", "sonnet", "flash", null, "本文", "ヘルパー", avatarField, true);

        if (engine == AgentEngineKind.Gemini)
        {
            return AgentDefinitionLoader.ParseSkillFile(plan.GeminiSkillFilePath!, AgentScope.Group, bundledDir).AvatarPath;
        }

        return AgentDefinitionLoader.Parse(File.ReadAllText(plan.ClaudeFilePath!), AgentScope.Group, plan.ClaudeFilePath!, bundledDir).AvatarPath;
    }

    [Fact]
    public void Gemini専用でも同梱画像が代替表示される()
    {
        var bundled = MakeBundledDir();
        var avatar = SaveAndLoadAvatar(AgentEngineKind.Gemini, null, bundled);
        Assert.Equal(Path.Combine(bundled, "helper.jpg"), avatar);
    }

    [Theory]
    [InlineData(AgentEngineKind.Claude, AgentEngineKind.Gemini)]
    [InlineData(AgentEngineKind.Gemini, AgentEngineKind.Claude)]
    [InlineData(AgentEngineKind.Shared, AgentEngineKind.Gemini)]
    [InlineData(AgentEngineKind.Shared, AgentEngineKind.Claude)]
    [InlineData(AgentEngineKind.Claude, AgentEngineKind.Shared)]
    [InlineData(AgentEngineKind.Gemini, AgentEngineKind.Shared)]
    public void 配備先を変えてもユーザー画像が移行先に保たれる(AgentEngineKind from, AgentEngineKind to)
    {
        var bundled = MakeBundledDir();
        var dropped = Path.Combine(_root, "dropped.webp");
        File.WriteAllBytes(dropped, [9, 8, 7]);

        // 変更前の配置で保存
        var first = SaveAndLoadAvatar(from, dropped, bundled, "helper.webp");
        Assert.NotNull(first);
        Assert.NotEqual(Path.Combine(bundled, "helper.jpg"), first);

        // 変更前の画像を現在の画像として、配置先を変更して保存
        var second = SaveAndLoadAvatar(to, first, bundled, "helper.webp");
        Assert.NotNull(second);
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(second!));
    }

    [Theory]
    [InlineData(AgentEngineKind.Claude, AgentEngineKind.Gemini)]
    [InlineData(AgentEngineKind.Gemini, AgentEngineKind.Claude)]
    [InlineData(AgentEngineKind.Shared, AgentEngineKind.Gemini)]
    [InlineData(AgentEngineKind.Gemini, AgentEngineKind.Shared)]
    public void 配備先を変えても同梱画像の代替表示が保たれる(AgentEngineKind from, AgentEngineKind to)
    {
        var bundled = MakeBundledDir();
        var expected = Path.Combine(bundled, "helper.jpg");
        Assert.Equal(expected, SaveAndLoadAvatar(from, null, bundled));
        Assert.Equal(expected, SaveAndLoadAvatar(to, null, bundled));
    }

    [Fact]
    public void アバターコピーは移行元を削除せず移行先に書く()
    {
        var src = Path.Combine(_root, "src.png");
        File.WriteAllBytes(src, [5]);
        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Gemini);
        AgentDeployWriter.CopyAvatarToTargets(plan, ClaudeDir, "helper", src);

        Assert.True(File.Exists(src));
        Assert.True(File.Exists(Path.Combine(SkillsDir, "helper", "helper.png")));
        Assert.True(File.Exists(Path.Combine(SkillsDir, "helper", "avatar.png")));
        Assert.False(File.Exists(Path.Combine(ClaudeDir, "helper.png")));
    }

    /// <summary>配備先をShared→Gemini専用→Claude専用→Sharedと変えても、両側のモデルと色が往復で保たれること。</summary>
    [Fact]
    public void 配備先を変えても非表示側のモデルと色が保持される()
    {
        void Save(AgentEngineKind engine)
        {
            var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", engine);
            AgentDeployWriter.Write(plan, ClaudeDir, SkillsDir, "helper", "説明", "Read", "opus", "pro", "#3B82F6", "本文", "ヘルパー", null, true);
        }

        AgentDefinition Load(AgentEngineKind engine) =>
            AgentDefinitionLoader.LoadScopeAgents(ClaudeDir, SkillsDir, AgentScope.Global).Single(a => a.Name == "helper");

        Save(AgentEngineKind.Shared);

        // Gemini専用で保存 → Claude側ファイルは消えるが、Claudeモデルは補助キーに残る
        Save(AgentEngineKind.Gemini);
        Assert.False(File.Exists(Path.Combine(ClaudeDir, "helper.md")));
        var g = Load(AgentEngineKind.Gemini);
        Assert.Equal(AgentEngineKind.Gemini, g.Engine);
        Assert.Equal("pro", g.Model);
        Assert.Equal("opus", g.ClaudeModel);
        Assert.Equal("#3B82F6", g.Color);

        // 編集画面の想定: 読み込んだ値をそのまま次の保存へ渡す
        void Resave(AgentEngineKind engine, AgentDefinition d)
        {
            var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", engine);
            var claude = d.Engine == AgentEngineKind.Gemini ? d.ClaudeModel : d.Model;
            var gemini = d.Engine == AgentEngineKind.Gemini ? d.Model : d.GeminiModel;
            AgentDeployWriter.Write(plan, ClaudeDir, SkillsDir, "helper", "説明", "Read", claude, gemini, d.Color, "本文", "ヘルパー", null, true);
        }

        Resave(AgentEngineKind.Claude, g);
        var c = Load(AgentEngineKind.Claude);
        Assert.Equal(AgentEngineKind.Claude, c.Engine);
        Assert.Equal("opus", c.Model);
        Assert.Equal("pro", c.GeminiModel);
        Assert.Equal("#3B82F6", c.Color);

        Resave(AgentEngineKind.Shared, c);
        var s = Load(AgentEngineKind.Shared);
        Assert.Equal(AgentEngineKind.Shared, s.Engine);
        Assert.Equal("opus", s.Model);
        Assert.Equal("pro", s.GeminiModel);
        Assert.Equal("#3B82F6", s.Color);
    }

    /// <summary>補助キーが空または無い既存ファイルは従来どおり読め、値はnullになること。</summary>
    [Fact]
    public void 補助キーが無い既存ファイルは従来どおり読める()
    {
        var plan = AgentDeployWriter.Plan(ClaudeDir, SkillsDir, "helper", AgentEngineKind.Gemini);
        AgentDeployWriter.Write(plan, ClaudeDir, SkillsDir, "helper", "説明", null, null, "flash", null, "本文", null, null, false);
        var g = AgentDefinitionLoader.LoadScopeAgents(ClaudeDir, SkillsDir, AgentScope.Global).Single();
        Assert.Null(g.ClaudeModel);
        Assert.Equal("flash", g.Model);
    }
}
