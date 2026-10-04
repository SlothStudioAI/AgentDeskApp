using Xunit;

namespace AgentDeskApp.Tests;

/// <summary>BUG-18: スタジオ全体でIdと呼び名(DisplayName)を一意にする判定のテスト。</summary>
public class AgentNameUniquenessTests
{
    private static StudioAgentEntry Entry(string name, string? displayName, string label = "TeamA", string? claudeDir = null)
    {
        var dir = claudeDir ?? $@"C:\studio\{label}\.claude\agents";
        var agent = new AgentDefinition(
            name, "desc", null, null, null, "body", AgentScope.Team, Path.Combine(dir, $"{name}.md"), displayName);
        return new StudioAgentEntry(agent, label, dir);
    }

    [Theory]
    [InlineData("QA担当 (イジワル)", "イジワル")]
    [InlineData("QA担当（イジワル）", "イジワル")]
    [InlineData("ツクル", "ツクル")]
    [InlineData("  調査担当 (ナレッジ) ", "ナレッジ")]
    [InlineData(null, "")]
    public void ExtractCallName_括弧内の呼び名または表示名全体を返す(string? input, string expected)
    {
        Assert.Equal(expected, AgentNameUniqueness.ExtractCallName(input));
    }

    [Fact]
    public void FindConflict_同じIdは大文字小文字を無視して衝突する()
    {
        var entries = new[] { Entry("qa-engineer", "QA担当 (イジワル)") };

        var conflict = AgentNameUniqueness.FindConflict("QA-Engineer", "別の人 (ベツ)", entries);

        Assert.NotNull(conflict);
        Assert.Equal(NameConflictKind.Id, conflict.Kind);
    }

    [Fact]
    public void FindConflict_呼び名が同じなら肩書きが違っても衝突する()
    {
        var entries = new[] { Entry("code-reviewer", "レビュー担当 (シラベ)") };

        var conflict = AgentNameUniqueness.FindConflict("sales-researcher", "リード調査担当 (シラベ)", entries);

        Assert.NotNull(conflict);
        Assert.Equal(NameConflictKind.DisplayName, conflict.Kind);
    }

    [Fact]
    public void FindConflict_全角半角の違いは同じ呼び名とみなす()
    {
        var entries = new[] { Entry("a", "担当A（ｼﾗﾍﾞ）") };

        Assert.NotNull(AgentNameUniqueness.FindConflict("b", "担当B (シラベ)", entries));
    }

    [Fact]
    public void FindConflict_似ているだけの呼び名は衝突しない()
    {
        var entries = new[] { Entry("code-reviewer", "レビュー担当 (シラベ)") };

        Assert.Null(AgentNameUniqueness.FindConflict("cs-researcher", "調査担当 (ナレッジ)", entries));
        Assert.Null(AgentNameUniqueness.FindConflict("x", "調査担当 (シラベル)", entries));
    }

    [Fact]
    public void FindConflict_自分自身は除外される()
    {
        var self = Entry("code-reviewer", "レビュー担当 (シラベ)");

        Assert.Null(AgentNameUniqueness.FindConflict(self.Agent.Name, self.Agent.DisplayName, [self], self.Agent));
    }

    [Fact]
    public void FindConflict_Id比較を無効にすると既存のId重複があっても呼び名修正ができる()
    {
        var self = Entry("dup", "旧名 (フルイ)", "TeamA");
        var other = Entry("dup", "別人 (ハコ)", "TeamB");

        Assert.Null(AgentNameUniqueness.FindConflict("dup", "新名 (アタラシ)", [self, other], self.Agent, checkId: false));
        Assert.NotNull(AgentNameUniqueness.FindConflict("dup", "新名 (アタラシ)", [self, other], self.Agent, checkId: true));
    }

    [Fact]
    public void FindConflict_異動先スコープに絞ると別チームの同名は無関係()
    {
        var mover = Entry("code-reviewer", "レビュー担当 (シラベ)", "TeamA");
        var sameNameOtherTeam = Entry("reviewer2", "査読 (シラベ)", "TeamB");
        var targetDir = sameNameOtherTeam.ClaudeDir;
        var unrelated = Entry("qa", "QA (イジワル)", "TeamC");

        var inTarget = AgentNameUniqueness.InScope([mover, sameNameOtherTeam, unrelated], targetDir).ToList();

        Assert.Single(inTarget);
        var conflict = AgentNameUniqueness.FindConflict(mover.Agent.Name, mover.Agent.DisplayName, inTarget, mover.Agent);
        Assert.NotNull(conflict);
        Assert.Equal("TeamB", conflict.Existing.ScopeLabel);
    }

    [Fact]
    public void FindDuplicates_Idと呼び名の重複を検出し重複なしなら空()
    {
        var clean = new[] { Entry("a", "A (エー)"), Entry("b", "B (ビー)", "TeamB") };
        Assert.Empty(AgentNameUniqueness.FindDuplicates(clean));

        var dirty = new[]
        {
            Entry("a", "A (エー)", "TeamA"),
            Entry("a", "C (シー)", "TeamB"),
            Entry("d", "D (エー)", "TeamC"),
        };

        var duplicates = AgentNameUniqueness.FindDuplicates(dirty);

        Assert.Contains(duplicates, d => d.Kind == NameConflictKind.Id && d.Members.Count == 2);
        Assert.Contains(duplicates, d => d.Kind == NameConflictKind.DisplayName && d.Key == "エー" && d.Members.Count == 2);
    }

    [Fact]
    public void FindDuplicates_同じ定義ファイルが二重に列挙されても重複扱いしない()
    {
        var e = Entry("a", "A (エー)");

        Assert.Empty(AgentNameUniqueness.FindDuplicates([e, e]));
    }

    [Fact]
    public void SuggestCopyDisplayName_衝突しない名前を連番で作る()
    {
        var source = Entry("qa", "QA担当 (イジワル)");
        var entries = new List<StudioAgentEntry> { source };

        var first = AgentNameUniqueness.SuggestCopyDisplayName("QA担当 (イジワル)", entries);
        Assert.Equal("QA担当 (イジワル)（コピー）", first);

        entries.Add(Entry("qa-copy", first));
        var second = AgentNameUniqueness.SuggestCopyDisplayName("QA担当 (イジワル)", entries);
        Assert.Equal("QA担当 (イジワル)（コピー2）", second);
        Assert.Null(AgentNameUniqueness.FindConflict(null, second, entries));
    }

    [Fact]
    public void SuggestCopyDisplayName_末尾が括弧の短い呼び名でも元の表示名を崩さない()
    {
        var entries = new List<StudioAgentEntry> { Entry("kaname", "要 (ヒ)") };

        Assert.Equal("要 (ヒ)（コピー）", AgentNameUniqueness.SuggestCopyDisplayName("要 (ヒ)", entries));
    }

    [Fact]
    public void SuggestCopyDisplayName_括弧が無い表示名は末尾にコピーを足す()
    {
        var entries = new List<StudioAgentEntry> { Entry("a", "ツクル") };

        Assert.Equal("ツクル（コピー）", AgentNameUniqueness.SuggestCopyDisplayName("ツクル", entries));
    }

    [Fact]
    public void FindDuplicatesInSameScope_別チーム間の同名は重複と判定せず同一チーム内のみ重複と判定する()
    {
        // TeamAとTeamBに同じId/呼び名が存在する（別グループ・別チーム間の同名）
        var crossTeamEntries = new[]
        {
            Entry("code-reviewer", "レビュー担当 (シラベ)", "TeamA"),
            Entry("code-reviewer", "レビュー担当 (シラベ)", "TeamB"),
            Entry("qa-engineer", "QA担当 (イジワル)", "TeamA"),
            Entry("qa-engineer", "QA担当 (イジワル)", "TeamB"),
        };

        // 他グループにいる同名はチェック不要（空リストが返る）
        var noDuplicatesAcrossScopes = AgentNameUniqueness.FindDuplicatesInSameScope(crossTeamEntries);
        Assert.Empty(noDuplicatesAcrossScopes);

        // TeamAの中で同名が重複しているケース
        var sameTeamEntries = new[]
        {
            Entry("code-reviewer", "レビュー担当 (シラベ)", "TeamA"),
            Entry("other-id", "査読担当 (シラベ)", "TeamA"), // 呼び名重複
            Entry("qa-engineer", "QA担当 (イジワル)", "TeamB"),
        };

        var duplicatesInSameScope = AgentNameUniqueness.FindDuplicatesInSameScope(sameTeamEntries);
        Assert.Single(duplicatesInSameScope);
        Assert.Equal(NameConflictKind.DisplayName, duplicatesInSameScope[0].Kind);
        Assert.Equal("シラベ", duplicatesInSameScope[0].Key);
    }
}
