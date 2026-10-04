using System.IO;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="AgentDefinitionLoader"/>のパース処理に関するテスト。
/// </summary>
public class AgentDefinitionLoaderTests
{
    [Fact]
    public void Parse_基本項目がすべて読み込める()
    {
        var text = """
            ---
            name: researcher
            description: 技術調査・比較を専門に行うエージェント。
            tools: WebSearch, WebFetch, Read
            model: sonnet
            color: blue
            ---

            あなたは技術調査・比較を専門に行うエージェントです。
            実装はせず、調査結果を簡潔にまとめて報告してください。
            """;

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Global, "dummy.md");

        Assert.Equal("researcher", agent.Name);
        Assert.Equal("技術調査・比較を専門に行うエージェント。", agent.Description);
        Assert.Equal("WebSearch, WebFetch, Read", agent.Tools);
        Assert.Equal("sonnet", agent.Model);
        Assert.Equal("blue", agent.Color);
        Assert.StartsWith("あなたは技術調査・比較を専門に行うエージェントです。", agent.Body);
        Assert.Equal(AgentScope.Global, agent.Scope);
    }

    [Fact]
    public void Parse_toolsがYAML箇条書き配列でもカンマ区切りに正規化される()
    {
        var text = """
            ---
            name: researcher
            description: 技術調査・比較を専門に行うエージェント。
            tools:
              - WebSearch
              - WebFetch
              - Read
            ---
            本文。
            """;

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Global, "dummy.md");

        Assert.Equal("WebSearch, WebFetch, Read", agent.Tools);
    }

    [Fact]
    public void Parse_displayNameがあれば読み込め表示名に使われる()
    {
        var text = """
            ---
            name: researcher
            description: 技術調査・比較を専門に行うエージェント。
            displayName: リサーチ担当のゆやぴよ
            ---
            本文。
            """;

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Global, "dummy.md");

        Assert.Equal("リサーチ担当のゆやぴよ", agent.DisplayName);
        Assert.Equal("リサーチ担当のゆやぴよ", agent.EffectiveDisplayName);
    }

    [Fact]
    public void EffectiveDisplayName_displayName未指定ならNameにフォールバックする()
    {
        var text = """
            ---
            name: researcher
            description: 技術調査・比較を専門に行うエージェント。
            ---
            本文。
            """;

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Global, "dummy.md");

        Assert.Null(agent.DisplayName);
        Assert.Equal("researcher", agent.EffectiveDisplayName);
    }

    [Fact]
    public void Save_displayNameを指定すると書き出され読み戻せる()
    {
        var path = Path.Combine(Path.GetTempPath(), "agent-desk-app-save-test-" + Guid.NewGuid() + ".md");
        try
        {
            AgentDefinitionLoader.Save(path, "researcher", "説明。", null, null, null, "本文。", "表示用の名前");

            var reloaded = AgentDefinitionLoader.ParseFile(path, AgentScope.Global);
            Assert.Equal("表示用の名前", reloaded.DisplayName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_未対応フィールドがあっても無視して読み込める()
    {
        var text = """
            ---
            name: icon-test
            description: 検証用エージェント。
            icon: test_face.png
            emoji: 🔍
            ---
            本文。
            """;

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Team, "dummy.md");

        Assert.Equal("icon-test", agent.Name);
        Assert.Null(agent.Tools);
        Assert.Null(agent.Model);
        Assert.Null(agent.Color);
    }

    [Fact]
    public void Parse_tools等の省略項目はnullになる()
    {
        var text = """
            ---
            name: minimal
            description: 最小構成のエージェント。
            ---
            本文のみ。
            """;

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Global, "dummy.md");

        Assert.Null(agent.Tools);
        Assert.Null(agent.Model);
        Assert.Null(agent.Color);
        Assert.Equal("本文のみ。", agent.Body);
    }

    [Fact]
    public void Parse_開始区切りが無いとFormatExceptionになる()
    {
        var text = "name: broken\n本文";

        Assert.Throws<FormatException>(() => AgentDefinitionLoader.Parse(text, AgentScope.Global, "dummy.md"));
    }

    [Fact]
    public void Parse_name欠落はFormatExceptionになる()
    {
        var text = """
            ---
            description: 名前が無い。
            ---
            本文。
            """;

        Assert.Throws<FormatException>(() => AgentDefinitionLoader.Parse(text, AgentScope.Global, "dummy.md"));
    }

    [Fact]
    public void LoadDirectory_フォルダが存在しない場合は空の一覧を返す()
    {
        var result = AgentDefinitionLoader.LoadDirectory(
            Path.Combine(Path.GetTempPath(), "agent-desk-app-not-exist-" + Guid.NewGuid()),
            AgentScope.Team);

        Assert.Empty(result);
    }

    [Fact]
    public void LoadDirectory_実ファイルからmdを読み込める()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "sample.md"), """
                ---
                name: sample
                description: サンプル。
                ---
                本文。
                """);

            var result = AgentDefinitionLoader.LoadDirectory(tempDir, AgentScope.Team);

            Assert.Single(result);
            Assert.Equal("sample", result[0].Name);
            Assert.Equal(AgentScope.Team, result[0].Scope);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ParseSkill_GeminiのSKILL_mdをパースできEngineがGeminiになる()
    {
        var text = """
            ---
            name: youtube-checker
            description: >-
              YouTube規約をチェックする
              専門エージェント。
            ---
            # YouTube規約チェッカー

            本文です。
            """;

        var agent = AgentDefinitionLoader.ParseSkill(text, AgentScope.Team, "dummy/SKILL.md");

        Assert.Equal("youtube-checker", agent.Name);
        Assert.Equal("YouTube規約をチェックする 専門エージェント。", agent.Description);
        Assert.Equal("YouTube規約チェッカー", agent.DisplayName);
        Assert.Equal(AgentEngineKind.Gemini, agent.Engine);
    }

    [Fact]
    public void ParseSkill_model欄を読み込める()
    {
        var text = """
            ---
            name: researcher
            description: 調査エージェント。
            model: flash_lite
            ---
            本文。
            """;

        var agent = AgentDefinitionLoader.ParseSkill(text, AgentScope.Team, "dummy/SKILL.md");

        Assert.Equal("flash_lite", agent.Model);
    }

    [Fact]
    public void SaveGeminiSkill_modelを指定すると書き出され読み戻せる()
    {
        var skillDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-gemini-model-" + Guid.NewGuid());
        try
        {
            AgentDefinitionLoader.SaveGeminiSkill(skillDir, "researcher", "説明。", "本文。", model: "pro");

            var agent = AgentDefinitionLoader.ParseSkillFile(Path.Combine(skillDir, "SKILL.md"), AgentScope.Team);

            Assert.Equal("pro", agent.Model);
        }
        finally
        {
            if (Directory.Exists(skillDir)) Directory.Delete(skillDir, recursive: true);
        }
    }

    [Fact]
    public void LoadScopeAgents_ClaudeとGeminiの両方に同名が存在する場合Sharedにマージされる()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-hybrid-" + Guid.NewGuid());
        var claudeDir = Path.Combine(baseDir, ".claude", "agents");
        var geminiDir = Path.Combine(baseDir, ".agents", "skills");

        Directory.CreateDirectory(claudeDir);
        Directory.CreateDirectory(Path.Combine(geminiDir, "shared-bot"));
        Directory.CreateDirectory(Path.Combine(geminiDir, "gemini-only"));

        try
        {
            // Claude側: shared-bot, claude-only
            File.WriteAllText(Path.Combine(claudeDir, "shared-bot.md"), """
                ---
                name: shared-bot
                description: 共有エージェント
                ---
                Claude本文
                """);
            File.WriteAllText(Path.Combine(claudeDir, "claude-only.md"), """
                ---
                name: claude-only
                description: Claude専用
                ---
                Claude専用本文
                """);

            // Gemini側: shared-bot, gemini-only
            File.WriteAllText(Path.Combine(geminiDir, "shared-bot", "SKILL.md"), """
                ---
                name: shared-bot
                description: 共有スキル
                model: pro
                ---
                # 共有スキル
                Gemini本文
                """);
            File.WriteAllText(Path.Combine(geminiDir, "gemini-only", "SKILL.md"), """
                ---
                name: gemini-only
                description: Gemini専用
                ---
                # Gemini専用
                Gemini専用本文
                """);

            var result = AgentDefinitionLoader.LoadScopeAgents(claudeDir, geminiDir, AgentScope.Group);

            Assert.Equal(3, result.Count);

            var claudeOnly = result.First(a => a.Name == "claude-only");
            Assert.Equal(AgentEngineKind.Claude, claudeOnly.Engine);

            var geminiOnly = result.First(a => a.Name == "gemini-only");
            Assert.Equal(AgentEngineKind.Gemini, geminiOnly.Engine);

            var sharedBot = result.First(a => a.Name == "shared-bot");
            Assert.Equal(AgentEngineKind.Shared, sharedBot.Engine);
            Assert.NotNull(sharedBot.GeminiSkillPath);
            Assert.Equal("pro", sharedBot.GeminiModel);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void SaveGeminiSkill_SKILL_mdが正しく生成され読み戻せる()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-gemini-skill-" + Guid.NewGuid());
        try
        {
            var skillFile = AgentDefinitionLoader.SaveGeminiSkill(
                tempDir,
                "video-bot",
                "動画編集を自動化するスキル。",
                "## 手順\n動画をレンダリングします。",
                "動画編集マスター",
                "#10B981");

            Assert.True(File.Exists(skillFile));

            var reloaded = AgentDefinitionLoader.ParseSkillFile(skillFile, AgentScope.Team);
            Assert.Equal("video-bot", reloaded.Name);
            Assert.Equal("動画編集を自動化するスキル。", reloaded.Description);
            Assert.Equal("動画編集マスター", reloaded.DisplayName);
            Assert.Equal(AgentEngineKind.Gemini, reloaded.Engine);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Save_アバター指定時にfrontmatterにavatarが出力され読み戻せる()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid()}.md");
        try
        {
            AgentDefinitionLoader.Save(
                tempFile,
                "designer-bot",
                "UIデザインを担当するエージェント",
                "Read, Write",
                "sonnet",
                "#3B82F6",
                "デザインを作成します。",
                "デザイナーBot",
                "designer-bot.png");

            Assert.True(File.Exists(tempFile));

            var reloaded = AgentDefinitionLoader.ParseFile(tempFile, AgentScope.Team);
            Assert.Equal("designer-bot", reloaded.Name);
            Assert.Equal("デザイナーBot", reloaded.DisplayName);
            // 同ディレクトリに画像ファイルがまだ実在しない場合、AvatarPathはnullか実ファイル検出
            var text = File.ReadAllText(tempFile);
            Assert.Contains("avatar: designer-bot.png", text);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveGeminiSkill_アバター指定時にfrontmatterに出力される()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agent-gemini-avatar-" + Guid.NewGuid());
        try
        {
            var skillFile = AgentDefinitionLoader.SaveGeminiSkill(
                tempDir,
                "avatar-bot",
                "アバターテストスキル",
                "テスト本文",
                "アバターボット",
                "#EC4899",
                "avatar.png");

            Assert.True(File.Exists(skillFile));
            var text = File.ReadAllText(skillFile);
            Assert.Contains("avatar: avatar.png", text);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void MoveAgent_Claude単独エージェントを移動先ディレクトリへ移動できる()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-move-claude-" + Guid.NewGuid());
        var sourceDir = Path.Combine(baseDir, "source", ".claude", "agents");
        var targetDir = Path.Combine(baseDir, "target", ".claude", "agents");
        Directory.CreateDirectory(sourceDir);
        try
        {
            var sourceFile = Path.Combine(sourceDir, "mover.md");
            File.WriteAllText(sourceFile, """
                ---
                name: mover
                description: 異動テスト用エージェント。
                ---
                本文。
                """);
            var agent = AgentDefinitionLoader.ParseFile(sourceFile, AgentScope.Team);

            AgentDefinitionLoader.MoveAgent(agent, targetDir, Path.Combine(baseDir, "target", ".agents", "skills"));

            Assert.False(File.Exists(sourceFile));
            Assert.True(File.Exists(Path.Combine(targetDir, "mover.md")));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void MoveAgent_移動先に同名定義が存在する場合はIOExceptionになり移動元は残る()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-move-conflict-" + Guid.NewGuid());
        var sourceDir = Path.Combine(baseDir, "source", ".claude", "agents");
        var targetDir = Path.Combine(baseDir, "target", ".claude", "agents");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);
        try
        {
            var sourceFile = Path.Combine(sourceDir, "mover.md");
            File.WriteAllText(sourceFile, """
                ---
                name: mover
                description: 異動テスト用エージェント。
                ---
                本文。
                """);
            File.WriteAllText(Path.Combine(targetDir, "mover.md"), """
                ---
                name: mover
                description: 移動先にすでに存在する同名定義。
                ---
                本文。
                """);
            var agent = AgentDefinitionLoader.ParseFile(sourceFile, AgentScope.Team);

            Assert.Throws<IOException>(() =>
                AgentDefinitionLoader.MoveAgent(agent, targetDir, Path.Combine(baseDir, "target", ".agents", "skills")));
            Assert.True(File.Exists(sourceFile));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void Parse_全角スペースでインデントした折返し行を新しいキーと誤認しない()
    {
        // 全角スペース(U+3000)字下げの継続行に「:」が含まれていても、別キー扱いにならず説明文に結合される (BUG-7)。
        var text = "---\nname: zen\ndescription: >-\n\u3000一行目の説明。\n\u3000注意: 二行目の説明。\nmodel: sonnet\n---\n本文。\n";

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Team, @"C:\x\zen.md");

        Assert.Equal("zen", agent.Name);
        Assert.Contains("一行目の説明。", agent.Description);
        Assert.Contains("注意: 二行目の説明。", agent.Description);
        Assert.Equal("sonnet", agent.Model);
    }

    [Fact]
    public void Parse_未閉じ引用符は孤立した引用符だけ除去して読み込みを継続する()
    {
        var text = "---\nname: quote-bot\ndescription: \"閉じ忘れの説明\nmodel: haiku\n---\n本文。\n";

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Team, @"C:\x\quote-bot.md");

        Assert.Equal("閉じ忘れの説明", agent.Description);
        Assert.Equal("haiku", agent.Model);
    }

    [Fact]
    public void Parse_ダッシュの後が全角スペースの箇条書きも配列として扱う()
    {
        var text = "---\nname: list-bot\ndescription: d\ntools:\n  -\u3000Read\n  -\u3000\"Write\"\n---\n本文。\n";

        var agent = AgentDefinitionLoader.Parse(text, AgentScope.Team, @"C:\x\list-bot.md");

        Assert.Equal("Read, Write", agent.Tools);
    }

    [Fact]
    public void MoveAgent_同位置へのドロップは何もせずfalseを返す()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-move-same-" + Guid.NewGuid());
        var sourceDir = Path.Combine(baseDir, ".claude", "agents");
        Directory.CreateDirectory(sourceDir);
        try
        {
            var sourceFile = Path.Combine(sourceDir, "mover.md");
            File.WriteAllText(sourceFile, "---\nname: mover\ndescription: d\n---\n本文。\n");
            var agent = AgentDefinitionLoader.ParseFile(sourceFile, AgentScope.Team);

            var moved = AgentDefinitionLoader.MoveAgent(agent, sourceDir, Path.Combine(baseDir, ".agents", "skills"));

            Assert.False(moved);
            Assert.True(File.Exists(sourceFile));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void MoveAgent_Shared_Gemini側に衝突があれば何も移動しない()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-move-shared-conflict-" + Guid.NewGuid());
        var srcClaude = Path.Combine(baseDir, "src", ".claude", "agents");
        var srcSkill = Path.Combine(baseDir, "src", ".agents", "skills", "dup");
        var dstClaude = Path.Combine(baseDir, "dst", ".claude", "agents");
        var dstSkills = Path.Combine(baseDir, "dst", ".agents", "skills");
        Directory.CreateDirectory(srcClaude);
        Directory.CreateDirectory(srcSkill);
        Directory.CreateDirectory(Path.Combine(dstSkills, "dup"));
        try
        {
            var claudeFile = Path.Combine(srcClaude, "dup.md");
            File.WriteAllText(claudeFile, "---\nname: dup\ndescription: d\n---\n本文。\n");
            File.WriteAllText(Path.Combine(srcSkill, "SKILL.md"), "---\nname: dup\ndescription: d\n---\n本文。\n");
            var agent = new AgentDefinition(
                "dup", "d", null, null, null, "本文。", AgentScope.Team, claudeFile,
                Engine: AgentEngineKind.Shared, GeminiSkillPath: Path.Combine(srcSkill, "SKILL.md"));

            Assert.Throws<IOException>(() => AgentDefinitionLoader.MoveAgent(agent, dstClaude, dstSkills));

            // Claude側だけ移動して分裂した状態になっていないこと
            Assert.True(File.Exists(claudeFile));
            Assert.False(File.Exists(Path.Combine(dstClaude, "dup.md")));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void MoveAgent_不正なエージェント名はArgumentExceptionで移動しない(string badName)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-move-traversal-" + Guid.NewGuid());
        var srcSkill = Path.Combine(baseDir, "src", ".agents", "skills", "victim");
        var dstSkills = Path.Combine(baseDir, "dst", ".agents", "skills");
        Directory.CreateDirectory(srcSkill);
        try
        {
            var skillFile = Path.Combine(srcSkill, "SKILL.md");
            File.WriteAllText(skillFile, "---\nname: victim\ndescription: d\n---\n本文。\n");
            var agent = new AgentDefinition(
                badName, "d", null, null, null, "本文。", AgentScope.Team, skillFile, Engine: AgentEngineKind.Gemini);

            Assert.Throws<ArgumentException>(() =>
                AgentDefinitionLoader.MoveAgent(agent, Path.Combine(baseDir, "dst", ".claude", "agents"), dstSkills));

            Assert.True(File.Exists(skillFile));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void AtomicFile_既存ファイルを置き換え一時ファイルを残さない()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent-desk-app-atomic-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.md");
            File.WriteAllText(path, "old");

            AtomicFile.WriteAllText(path, "新しい内容");

            Assert.Equal("新しい内容", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MoveAgent_Geminiスキルをフォルダごと移動先へ移動できる()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "agent-desk-app-move-gemini-" + Guid.NewGuid());
        var sourceSkillsDir = Path.Combine(baseDir, "source", ".agents", "skills");
        var targetSkillsDir = Path.Combine(baseDir, "target", ".agents", "skills");
        var sourceSkillDir = Path.Combine(sourceSkillsDir, "mover-skill");
        Directory.CreateDirectory(sourceSkillDir);
        try
        {
            var skillFile = Path.Combine(sourceSkillDir, "SKILL.md");
            File.WriteAllText(skillFile, """
                ---
                name: mover-skill
                description: 異動テスト用Geminiスキル。
                ---
                本文。
                """);
            var agent = AgentDefinitionLoader.ParseSkillFile(skillFile, AgentScope.Team);

            AgentDefinitionLoader.MoveAgent(agent, Path.Combine(baseDir, "target", ".claude", "agents"), targetSkillsDir);

            Assert.False(Directory.Exists(sourceSkillDir));
            Assert.True(File.Exists(Path.Combine(targetSkillsDir, "mover-skill", "SKILL.md")));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>
    /// 同梱アバターのテスト用に、チームフォルダ(定義ファイル置き場)と同梱画像フォルダを一時作成する。
    /// </summary>
    /// <param name="agentName">定義ファイルに書く name(同梱画像のファイル名にも使う)。</param>
    /// <param name="frontmatterAvatar">frontmatter の avatar 値。null なら書かない。</param>
    /// <param name="createBundledImage">同梱フォルダに {agentName}.jpg を置くかどうか。</param>
    /// <returns>(一時ルート, 定義ファイルのパス, 同梱画像フォルダ)</returns>
    private static (string Root, string FilePath, string BundledDir) CreateBundledAvatarFixture(
        string agentName, string? frontmatterAvatar, bool createBundledImage)
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-bundled-avatar-" + Guid.NewGuid());
        var teamDir = Path.Combine(root, "team", ".claude", "agents");
        var bundledDir = Path.Combine(root, "Assets", "Avatars");
        Directory.CreateDirectory(teamDir);
        Directory.CreateDirectory(bundledDir);

        var avatarLine = frontmatterAvatar is null ? "" : $"avatar: {frontmatterAvatar}\n";
        var filePath = Path.Combine(teamDir, agentName + ".md");
        File.WriteAllText(filePath, $"---\nname: {agentName}\ndescription: 同梱アバターのテスト用。\n{avatarLine}---\n本文。\n");

        if (createBundledImage)
        {
            File.WriteAllBytes(Path.Combine(bundledDir, agentName + ".jpg"), [0xFF, 0xD8, 0xFF]);
        }

        return (root, filePath, bundledDir);
    }

    [Fact]
    public void Parse_frontmatter指定も同名画像も無いとき同梱画像を使う()
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("software-engineer", null, createBundledImage: true);
        try
        {
            var agent = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);

            Assert.Equal(Path.Combine(bundledDir, "software-engineer.jpg"), agent.AvatarPath);
            // コピーはしない(チームフォルダに画像が増えていない)
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(filePath)!, "software-engineer.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_frontmatterで指定した画像があれば同梱画像を使わない()
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("software-engineer", "my-face.png", createBundledImage: true);
        try
        {
            var own = Path.Combine(Path.GetDirectoryName(filePath)!, "my-face.png");
            File.WriteAllBytes(own, [0x89, 0x50]);

            var agent = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);

            Assert.Equal(own, agent.AvatarPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_定義ファイルと同名の画像があれば同梱画像を使わない()
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("software-engineer", null, createBundledImage: true);
        try
        {
            var sibling = Path.Combine(Path.GetDirectoryName(filePath)!, "software-engineer.png");
            File.WriteAllBytes(sibling, [0x89, 0x50]);

            var agent = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);

            Assert.Equal(sibling, agent.AvatarPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_同梱画像も無いときAvatarPathはnull()
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("no-image-agent", null, createBundledImage: false);
        try
        {
            var agent = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);

            Assert.Null(agent.AvatarPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsBundledAvatarPath_同梱フォルダ内の画像だけtrueになる()
    {
        var bundledDir = Path.Combine(Path.GetTempPath(), "bundled-check", "Assets", "Avatars");

        Assert.True(AgentDefinitionLoader.IsBundledAvatarPath(Path.Combine(bundledDir, "a.jpg"), bundledDir));
        Assert.False(AgentDefinitionLoader.IsBundledAvatarPath(Path.Combine(Path.GetTempPath(), "team", "a.jpg"), bundledDir));
        Assert.False(AgentDefinitionLoader.IsBundledAvatarPath(null, bundledDir));
    }

    [Fact]
    public void DeleteAgentAndOrphanedAvatar_同梱画像の代替表示なら画像を消さない()
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("software-engineer", null, createBundledImage: true);
        try
        {
            var agent = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);

            AgentDefinitionLoader.DeleteAgentAndOrphanedAvatar(agent, [agent], bundledDir);

            Assert.False(File.Exists(filePath));
            Assert.True(File.Exists(Path.Combine(bundledDir, "software-engineer.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadScopeAgents_Claude側が同梱画像ならGemini側で設定した画像を優先する()
    {
        // 既定の同梱フォルダ(テスト出力の Assets\Avatars)にある software-engineer.jpg を使う
        Assert.NotNull(AgentDefinitionLoader.FindBundledAvatar("software-engineer"));

        var root = Path.Combine(Path.GetTempPath(), "agent-bundled-merge-" + Guid.NewGuid());
        var claudeDir = Path.Combine(root, ".claude", "agents");
        var skillsDir = Path.Combine(root, ".agents", "skills");
        var skillDir = Path.Combine(skillsDir, "software-engineer");
        Directory.CreateDirectory(claudeDir);
        Directory.CreateDirectory(skillDir);
        try
        {
            File.WriteAllText(Path.Combine(claudeDir, "software-engineer.md"), "---\nname: software-engineer\ndescription: テスト。\n---\n本文。\n");
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: software-engineer\ndescription: テスト。\n---\n本文。\n");
            var geminiAvatar = Path.Combine(skillDir, "avatar.png");
            File.WriteAllBytes(geminiAvatar, [0x89, 0x50]);

            var agents = AgentDefinitionLoader.LoadScopeAgents(claudeDir, skillsDir, AgentScope.Team);

            var merged = Assert.Single(agents);
            Assert.Equal(AgentEngineKind.Shared, merged.Engine);
            Assert.Equal(geminiAvatar, merged.AvatarPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("NONE")]
    public void Parse_avatarがnoneなら同名画像や同梱画像があってもnullになる(string marker)
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("software-engineer", marker, createBundledImage: true);
        try
        {
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(filePath)!, "software-engineer.png"), [0x89, 0x50]);

            var agent = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);

            Assert.Null(agent.AvatarPath);
            Assert.True(agent.AvatarDisabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_none以外の指定ではAvatarDisabledはfalseのまま()
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("software-engineer", "my-face.png", createBundledImage: true);
        try
        {
            var own = Path.Combine(Path.GetDirectoryName(filePath)!, "my-face.png");
            File.WriteAllBytes(own, [0x89, 0x50]);

            var agent = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);

            Assert.Equal(own, agent.AvatarPath);
            Assert.False(agent.AvatarDisabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ParseSkill_avatarがnoneならフォルダ内の画像を使わない()
    {
        var skillDir = Path.Combine(Path.GetTempPath(), "agent-skill-none-" + Guid.NewGuid());
        Directory.CreateDirectory(skillDir);
        try
        {
            File.WriteAllBytes(Path.Combine(skillDir, "avatar.png"), [0x89, 0x50]);
            var skillFile = Path.Combine(skillDir, "SKILL.md");
            File.WriteAllText(skillFile, "---\nname: s1\ndescription: テスト。\navatar: none\n---\n本文。\n");

            var agent = AgentDefinitionLoader.ParseSkillFile(skillFile, AgentScope.Team);

            Assert.Null(agent.AvatarPath);
            Assert.True(agent.AvatarDisabled);
        }
        finally
        {
            Directory.Delete(skillDir, recursive: true);
        }
    }

    [Fact]
    public void LoadScopeAgents_Claude側がnoneならGemini側の画像も使わない()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-none-merge-" + Guid.NewGuid());
        var claudeDir = Path.Combine(root, ".claude", "agents");
        var skillsDir = Path.Combine(root, ".agents", "skills");
        var skillDir = Path.Combine(skillsDir, "software-engineer");
        Directory.CreateDirectory(claudeDir);
        Directory.CreateDirectory(skillDir);
        try
        {
            File.WriteAllText(Path.Combine(claudeDir, "software-engineer.md"), "---\nname: software-engineer\ndescription: テスト。\navatar: none\n---\n本文。\n");
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: software-engineer\ndescription: テスト。\n---\n本文。\n");
            File.WriteAllBytes(Path.Combine(skillDir, "avatar.png"), [0x89, 0x50]);

            var merged = Assert.Single(AgentDefinitionLoader.LoadScopeAgents(claudeDir, skillsDir, AgentScope.Team));

            Assert.Equal(AgentEngineKind.Shared, merged.Engine);
            Assert.Null(merged.AvatarPath);
            Assert.True(merged.AvatarDisabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 保存_解除するとavatar_noneが書かれ標準の画像に戻すと外れる()
    {
        var (root, filePath, bundledDir) = CreateBundledAvatarFixture("software-engineer", null, createBundledImage: true);
        try
        {
            var bundled = Path.Combine(bundledDir, "software-engineer.jpg");

            // 1) 「×」で解除して保存 → avatar: none が書かれ、読み直しても同梱画像が出ない
            var removedValue = AgentDefinitionLoader.ResolveAvatarFieldForSave(
                avatarRemoved: true, currentAvatarPath: null, safeName: "software-engineer",
                existingAvatarPath: bundled, bundledAvatarDirectory: bundledDir);
            Assert.Equal("none", removedValue);
            AgentDefinitionLoader.Save(filePath, "software-engineer", "テスト。", null, null, null, "本文。", avatar: removedValue);
            Assert.Contains("avatar: none", File.ReadAllText(filePath));
            var afterRemove = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);
            Assert.Null(afterRemove.AvatarPath);
            Assert.True(afterRemove.AvatarDisabled);

            // 2) 「標準の画像に戻す」で保存 → avatar を書かず、読み直すと同梱画像に戻る
            var restoredValue = AgentDefinitionLoader.ResolveAvatarFieldForSave(
                avatarRemoved: false, currentAvatarPath: bundled, safeName: "software-engineer",
                existingAvatarPath: afterRemove.AvatarPath, bundledAvatarDirectory: bundledDir);
            Assert.Null(restoredValue);
            AgentDefinitionLoader.Save(filePath, "software-engineer", "テスト。", null, null, null, "本文。", avatar: restoredValue);
            Assert.DoesNotContain("avatar:", File.ReadAllText(filePath));
            var afterRestore = AgentDefinitionLoader.Parse(File.ReadAllText(filePath), AgentScope.Team, filePath, bundledDir);
            Assert.Equal(bundled, afterRestore.AvatarPath);
            Assert.False(afterRestore.AvatarDisabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 保存_none状態で新しく画像を選ぶとnoneが外れ選んだ画像のファイル名になる()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-none-select-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var picked = Path.Combine(root, "Picked.WEBP");
            File.WriteAllBytes(picked, [0x52, 0x49]);

            var value = AgentDefinitionLoader.ResolveAvatarFieldForSave(
                avatarRemoved: false, currentAvatarPath: picked, safeName: "helper",
                existingAvatarPath: null, bundledAvatarDirectory: Path.Combine(root, "Assets", "Avatars"));

            Assert.Equal("helper.webp", value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 保存_画像を変えずに保存すると既存の個別画像のファイル名を引き継ぐ()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-keep-avatar-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var existing = Path.Combine(root, "my-face.png");
            File.WriteAllBytes(existing, [0x89, 0x50]);
            var bundledDir = Path.Combine(root, "Assets", "Avatars");

            // 現在の選択が無い(読み込み失敗等)場合は既存の個別画像を引き継ぐ
            Assert.Equal("my-face.png", AgentDefinitionLoader.ResolveAvatarFieldForSave(
                false, null, "helper", existing, bundledDir));
            // 既存が同梱画像なら何も書かない
            Assert.Null(AgentDefinitionLoader.ResolveAvatarFieldForSave(
                false, null, "helper", Path.Combine(bundledDir, "helper.jpg"), bundledDir));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveGeminiSkill_解除時はavatar_noneが書かれ読み直すと画像なしになる()
    {
        var skillDir = Path.Combine(Path.GetTempPath(), "agent-gemini-none-" + Guid.NewGuid());
        try
        {
            var skillFile = AgentDefinitionLoader.SaveGeminiSkill(skillDir, "s1", "テスト。", "本文。", avatar: AgentDefinitionLoader.NoAvatarMarker);

            Assert.Contains("avatar: none", File.ReadAllText(skillFile));
            var agent = AgentDefinitionLoader.ParseSkillFile(skillFile, AgentScope.Team);
            Assert.True(agent.AvatarDisabled);
            Assert.Null(agent.AvatarPath);
        }
        finally
        {
            if (Directory.Exists(skillDir)) Directory.Delete(skillDir, recursive: true);
        }
    }
}
