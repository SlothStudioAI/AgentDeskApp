using Xunit;

namespace AgentDeskApp.Tests;

/// <summary>検知用の候補メンバー一覧(全スコープ登録・同一Idの統合・置換)のテスト。</summary>
public class KnownMemberCandidateRegistryTests
{
    private static AgentDefinition Agent(string name, string? displayName, AgentScope scope = AgentScope.Team) =>
        new(name, "desc", null, null, null, "body", scope, $@"C:\studio\{name}.md", displayName);

    [Fact]
    public void Replace_複数スコープのメンバーがすべて登録される()
    {
        var registry = new KnownMemberCandidateRegistry();

        registry.Replace([
            Agent("global-pm", "PM (ナビ)", AgentScope.Global),
            Agent("ws-qa", "QA担当 (イジワル)", AgentScope.Group),
            Agent("it-dev", "実装担当 (ツクル)", AgentScope.Team),
        ]);

        var ids = registry.Snapshot().Select(c => c.Id).OrderBy(i => i).ToList();
        Assert.Equal(["global-pm", "it-dev", "ws-qa"], ids);
    }

    [Fact]
    public void Replace_同一Idは1件にまとまり後のものが優先される()
    {
        var registry = new KnownMemberCandidateRegistry();

        registry.Replace([Agent("dup", "旧"), Agent("dup", "新")]);

        var only = Assert.Single(registry.Snapshot());
        Assert.Equal("新", only.DisplayName);
    }

    [Fact]
    public void Replace_再読み込みで削除済みメンバーが消え改名が反映される()
    {
        var registry = new KnownMemberCandidateRegistry();
        registry.Replace([Agent("a", "エー"), Agent("b", "ビー")]);

        registry.Replace([Agent("a", "エー改")]);

        var only = Assert.Single(registry.Snapshot());
        Assert.Equal("a", only.Id);
        Assert.Equal("エー改", only.DisplayName);
    }

    [Fact]
    public void Remember_累積登録では既存の候補が残る()
    {
        var registry = new KnownMemberCandidateRegistry();
        registry.Remember([Agent("a", "エー")]);

        registry.Remember([Agent("b", "ビー")]);

        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void 登録した候補で指示形の担当表記が検知される()
    {
        var registry = new KnownMemberCandidateRegistry();
        registry.Replace([Agent("tsukuru", "ソフトウェア担当 (ツクル)")]);

        var detected = PromptMemberNominationDetector.Detect("担当: ツクル（実装担当）\n機能Xを実装", registry.Snapshot());

        Assert.Contains("tsukuru", detected);
    }

    [Fact]
    public void Replace_同一Idで後の定義の表示名が空でも先の表示名の呼び名で検知できる()
    {
        // 実機の再現: software-engineerがIT開発(表示名あり)と他のチーム(表示名なし)の両方に存在し、後者で上書きされていた
        var registry = new KnownMemberCandidateRegistry();
        registry.Replace([
            Agent("software-engineer", "実装担当 (ツクル)"),
            Agent("software-engineer", ""),
            Agent("software-engineer", null),
        ]);

        var only = Assert.Single(registry.Snapshot());
        Assert.Equal("実装担当 (ツクル)", only.DisplayName);
        var detected = PromptMemberNominationDetector.Detect("担当: ツクル（実装担当）\n読み取りだけの作業です", registry.Snapshot());
        Assert.Contains("software-engineer", detected);
    }

    [Fact]
    public void Replace_同一Idで表示名が異なる場合はどちらの呼び名でも検知できる()
    {
        var registry = new KnownMemberCandidateRegistry();
        registry.Replace([Agent("dev", "実装担当 (ツクル)"), Agent("dev", "開発 (カイハツ)")]);

        Assert.Contains("dev", PromptMemberNominationDetector.Detect("担当: ツクル", registry.Snapshot()));
        Assert.Contains("dev", PromptMemberNominationDetector.Detect("担当: カイハツ", registry.Snapshot()));
    }

    [Fact]
    public void 候補更新は件数が変わったときだけ診断ログに出る()
    {
        var path = Path.Combine(Path.GetTempPath(), "registry-diag-" + Guid.NewGuid() + ".log");
        try
        {
            var registry = new KnownMemberCandidateRegistry { Log = new DiagnosticLog(path, true, 100000) };

            registry.Replace([Agent("a", "エー"), Agent("b", "ビー")]);
            registry.Replace([Agent("a", "エー"), Agent("b", "ビー")]);
            registry.Remember([Agent("c", "シー")]);

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith("候補更新 count=2", lines[0]);
            Assert.EndsWith("候補更新 count=3", lines[1]);
            Assert.DoesNotContain("エー", string.Join("", lines));
        }
        finally { File.Delete(path); }
    }
}
