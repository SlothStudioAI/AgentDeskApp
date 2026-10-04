using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="PromptMemberNominationDetector"/>(Agent/Task命令文からのメンバー名検出)の単体テスト(BUG-8)。
/// </summary>
public class PromptMemberNominationDetectorTests
{
    private static readonly PromptNominationCandidate[] Members =
    [
        new("tsukuru", "ツクル"),
        new("shirabe", "シラベ"),
        new("tsutae", "ツタエ"),
        new("ijiwaru", "イジワル"),
    ];

    private static IReadOnlySet<string> Detect(string? prompt) => PromptMemberNominationDetector.Detect(prompt, Members);

    [Theory]
    [InlineData("ツクルに実装を頼んで")]
    [InlineData("ツクルへ依頼です")]
    [InlineData("ツクルでビルドして")]
    [InlineData("ツクルさん、お願いします")]
    [InlineData("ツクル君にやらせて")]
    [InlineData("ツクルくんに聞いて")]
    [InlineData("ツクルちゃんお願い")]
    [InlineData("@ツクル バグを直して")]
    [InlineData("ツクル、これを直して")]
    [InlineData("ツクルは実装をしてください")]
    [InlineData("ツクルがバグを直して")]
    [InlineData("まず調べてから、ツクルに渡して")]
    [InlineData("@tsukuru fix it")]
    [InlineData("tsukuruに任せる")]
    public void 指示形で名前が現れると拾う(string prompt)
    {
        Assert.Contains("tsukuru", Detect(prompt));
    }

    [Theory]
    [InlineData("ツクルの設計を参考に実装して")]
    [InlineData("ツクルさんの設計を参考にして")]
    [InlineData("ツクルについて説明して")]
    [InlineData("ツクルに近い構成にして")]
    [InlineData("ツクルは優秀です")]
    [InlineData("ツクルを参考にする")]
    [InlineData("ツクル")]
    public void 単なる言及は拾わない(string prompt)
    {
        Assert.DoesNotContain("tsukuru", Detect(prompt));
    }

    [Theory]
    [InlineData("メタツクルに頼んで")]
    [InlineData("ツクルコに頼んで")]
    [InlineData("ツクルーに頼んで")]
    [InlineData("xtsukuruに頼んで")]
    [InlineData("tsukuru2に頼んで")]
    [InlineData("@ツクルコ お願い")]
    public void 別の単語の一部としての部分一致は拾わない(string prompt)
    {
        Assert.DoesNotContain("tsukuru", Detect(prompt));
    }

    [Fact]
    public void 名前が別メンバーの部分文字列でも長い名前のメンバーだけ拾う()
    {
        var candidates = new[] { new PromptNominationCandidate("a", "ツク"), new PromptNominationCandidate("b", "ツクル") };

        var result = PromptMemberNominationDetector.Detect("ツクルに頼んで", candidates);

        Assert.Contains("b", result);
        Assert.DoesNotContain("a", result);
    }

    [Theory]
    [InlineData("ﾂｸﾙに頼んで")] // 半角カナ
    [InlineData("ＴＳＵＫＵＲＵに頼んで")] // 全角英字
    [InlineData("@TSUKURU fix")] // 大文字
    [InlineData("ツクルに頼んで")]
    public void 全角半角と大文字小文字の違いを無視して拾う(string prompt)
    {
        Assert.Contains("tsukuru", Detect(prompt));
    }

    [Fact]
    public void 表示名とIdのどちらでも拾い結果はIdで返る()
    {
        Assert.Contains("shirabe", Detect("shirabeに調べて"));
        Assert.Contains("shirabe", Detect("シラベに調べて"));
    }

    [Fact]
    public void 複数メンバーの同時指名を拾う()
    {
        var result = Detect("ツクルに実装、シラベさんに調査を頼んで。ツタエの資料を参考に");

        Assert.Contains("tsukuru", result);
        Assert.Contains("shirabe", result);
        Assert.DoesNotContain("tsutae", result);
    }

    [Fact]
    public void 最初の出現が言及でも後の出現が指示形なら拾う()
    {
        Assert.Contains("tsukuru", Detect("ツクルの設計を見て、ツクルに実装を頼んで"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空のプロンプトは空集合(string? prompt)
    {
        Assert.Empty(Detect(prompt));
    }

    [Fact]
    public void 名前が短すぎる候補は対象外()
    {
        var result = PromptMemberNominationDetector.Detect("君に頼んで", [new PromptNominationCandidate("x", "君")]);

        Assert.Empty(result);
    }

    private static readonly PromptNominationCandidate[] RoleMembers =
    [
        new("tsukuru", "ソフトウェア担当 (ツクル)"),
        new("ijiwaru", "QA担当（イジワル）"),
        new("qa", "QA"),
        new("pm", "プロジェクト管理"),
    ];

    [Theory]
    [InlineData("イジワルに確認を頼んで", "ijiwaru")]
    [InlineData("ツクルさん、お願いします", "tsukuru")]
    [InlineData("@ツクル 直して", "tsukuru")]
    public void 表示名の括弧内の呼び名で拾い結果はIdで返る(string prompt, string expectedId)
    {
        var result = PromptMemberNominationDetector.Detect(prompt, RoleMembers);

        Assert.Contains(expectedId, result);
    }

    [Fact]
    public void 括弧の無い表示名は従来どおり全体で照合する()
    {
        Assert.Contains("pm", PromptMemberNominationDetector.Detect("プロジェクト管理に頼んで", RoleMembers));
    }

    [Theory]
    [InlineData("ツクルで作った資料を読んで")]
    [InlineData("ツクルで十分です")]
    public void 助詞のでは依頼表現が無ければ拾わない(string prompt)
    {
        Assert.DoesNotContain("tsukuru", Detect(prompt));
    }

    [Theory]
    [InlineData("ツクルは実装担当です")]
    [InlineData("ツクルが担当")]
    [InlineData("ツクルは実装が得意")]
    public void 名詞の実装や担当だけでは依頼とみなさない(string prompt)
    {
        Assert.DoesNotContain("tsukuru", Detect(prompt));
    }

    [Theory]
    [InlineData("ツクルは実装をお願いします")]
    [InlineData("ツクルが実装して")]
    [InlineData("ツクルで実装して")]
    public void 動詞の依頼表現があれば拾う(string prompt)
    {
        Assert.Contains("tsukuru", Detect(prompt));
    }

    [Theory]
    [InlineData("ツクル、シラベ、イジワルの3人")]
    [InlineData("ツクル、 シラベ")]
    public void 名前の列挙は拾わない(string prompt)
    {
        var result = Detect(prompt);

        Assert.DoesNotContain("tsukuru", result);
        Assert.DoesNotContain("shirabe", result);
    }

    [Fact]
    public void 列挙の後の別メンバーへの依頼は拾う()
    {
        var result = Detect("ツクル、シラベ、イジワルに調べて");

        Assert.Contains("ijiwaru", result);
    }

    [Fact]
    public void 英字2文字のIdは照合対象外()
    {
        var result = PromptMemberNominationDetector.Detect("qaに確認して。@pm お願い", RoleMembers);

        Assert.DoesNotContain("qa", result);
        Assert.DoesNotContain("pm", result);
    }

    [Theory]
    [InlineData("あなたは開発チームのソフトウェアエンジニア『ツクル』です。")]
    [InlineData("あなたはソフトウェアエンジニアのツクルです")]
    [InlineData("ツクルとして実装してください")]
    [InlineData("担当: ツクル")]
    [InlineData("担当：ツクル")]
    [InlineData("【ツクル】設計レビュー")]
    [InlineData("「ツクル」として振る舞う")]
    [InlineData("ツクル役で実装")]
    public void 役割宣言形で名前が現れると拾う(string instruction)
    {
        Assert.Contains("tsukuru", Detect(instruction));
    }

    [Theory]
    [InlineData("ツクルの設計を参考にしてください")]
    [InlineData("「ツクル」の設計を参考に")]
    public void 役割宣言に見えない言及は拾わない(string instruction)
    {
        Assert.DoesNotContain("tsukuru", Detect(instruction));
    }
}
