using System.Text.Encodings.Web;
using System.Text.Json;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// Gemini (Antigravity) の invoke_subagent で TypeName="self" などの汎用の型が指定され、
/// 役割が Role に書かれた場合でも、担当メンバーのカードが Running→Done になることを確認するテスト。
/// transcript.jsonl の行は実ログと同じ構造(args.Subagents が「JSON配列を表す文字列」)で組み立てる。
/// </summary>
[Collection("UserProfileEnv")]
public class GeminiSubagentRoleDetectionTests
{
    /// <summary>照合対象のメンバー(テンプレートと同じ「役割 (呼び名)」形式の表示名)。</summary>
    private static readonly PromptNominationCandidate[] Members =
    [
        new("visual-designer", "ビジュアル担当 (エガク)"),
        new("tsutae", "ドキュメント・整理担当（ツタエ）"),
        new("shirabe", "コードレビュー担当 (シラベ)"),
    ];

    /// <summary>日本語をエスケープせずに書き出す(実ログと同じ見た目にする)ためのJSON設定。</summary>
    private static readonly JsonSerializerOptions RawJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 実ログ形式の invoke_subagent 行を作る(Subagents は配列をJSON文字列化した値)。
    /// </summary>
    /// <param name="subagentsJson">Subagents に入れるJSON配列の文字列(生のまま)。</param>
    /// <param name="status">行のstatus。</param>
    private static string InvokeLine(string subagentsJson, string status = "DONE") =>
        JsonSerializer.Serialize(new
        {
            step_index = 10,
            source = "MODEL",
            type = "PLANNER_RESPONSE",
            status,
            created_at = "2026-10-03T00:00:00Z",
            tool_calls = new[]
            {
                new
                {
                    name = "invoke_subagent",
                    args = new { Subagents = subagentsJson, toolAction = "\"Delegating\"", toolSummary = "\"Invoke\"" },
                },
            },
        }, RawJson);

    /// <summary>Subagents の1要素(Model/Prompt/Role/TypeName)をJSON配列文字列にする。Roleがnullなら項目ごと省く。</summary>
    /// <param name="typeName">TypeName の値。</param>
    /// <param name="role">Role の値(null可)。</param>
    private static string Subagents(string typeName, string? role)
    {
        var item = new Dictionary<string, string> { ["Model"] = "inherit", ["Prompt"] = "画像を作ってください。" };
        if (role is not null)
        {
            item["Role"] = role;
        }

        item["TypeName"] = typeName;
        return JsonSerializer.Serialize(new[] { item }, RawJson);
    }

    /// <summary>Gemini側の最後の応答行(ツール呼び出しなし=完了・入力待ち)。</summary>
    private const string ModelDoneLine =
        "{\"step_index\":11,\"source\":\"MODEL\",\"type\":\"PLANNER_RESPONSE\",\"status\":\"DONE\",\"content\":\"完了しました\"}";

    // ===== Scanner: 実ログ形式の行からの抽出 =====

    [Fact]
    public void Scanner_TypeNameがselfでRoleありの行からTypeNameとRoleの組を取り出せる()
    {
        var result = GeminiSessionScanner.ExtractSubagentInvocations(InvokeLine(Subagents("self", "ビジュアル担当 (エガク)")));

        Assert.Equal([new GeminiSubagentInvocation("self", "ビジュアル担当 (エガク)")], result);
    }

    [Fact]
    public void Scanner_TypeNameがメンバー名の行はそのまま取り出せる()
    {
        var result = GeminiSessionScanner.ExtractSubagentInvocations(InvokeLine(Subagents("tsutae", "ドキュメント・整理担当（ツタエ）")));

        Assert.Equal([new GeminiSubagentInvocation("tsutae", "ドキュメント・整理担当（ツタエ）")], result);
    }

    [Fact]
    public void Scanner_Roleなしの行はRoleがnullになる()
    {
        var result = GeminiSessionScanner.ExtractSubagentInvocations(InvokeLine(Subagents("self", null)));

        Assert.Equal([new GeminiSubagentInvocation("self", null)], result);
    }

    [Fact]
    public void Scanner_Subagentsが配列そのものの形式でも取り出せる()
    {
        var line = "{\"tool_calls\":[{\"name\":\"invoke_subagent\",\"args\":{\"Subagents\":[{\"TypeName\":\"self\",\"Role\":\"エガク\"}]}}]}";

        Assert.Equal([new GeminiSubagentInvocation("self", "エガク")], GeminiSessionScanner.ExtractSubagentInvocations(line));
    }

    [Fact]
    public void Scanner_Promptが省略されてJSONが壊れていてもTypeNameとRoleを拾える()
    {
        // 実ログでは長いPromptが途中で「<truncated N bytes>」に置き換わり、生の改行を含む壊れたJSON文字列になる
        var broken = "[{\"Model\":\"flash\",\"Prompt\":\"調査してください。\\n\\n詳細\n<truncated 846 bytes>\",\"Role\":\"リサーチ担当 (サガス)\",\"TypeName\":\"research\"}]";

        var result = GeminiSessionScanner.ExtractSubagentInvocations(InvokeLine(broken));

        Assert.Equal([new GeminiSubagentInvocation("research", "リサーチ担当 (サガス)")], result);
    }

    [Fact]
    public void Scanner_Prompt本文中のRoleという文字列には反応しない()
    {
        var item = new Dictionary<string, string>
        {
            ["Prompt"] = "例: {\"Role\":\"ビジュアル担当 (エガク)\"} のように書く",
            ["TypeName"] = "self",
        };
        var result = GeminiSessionScanner.ExtractSubagentInvocations(InvokeLine(JsonSerializer.Serialize(new[] { item }, RawJson)));

        Assert.Equal([new GeminiSubagentInvocation("self", null)], result);
    }

    [Fact]
    public void Scanner_invoke_subagent以外の行からは何も取り出さない()
    {
        Assert.Empty(GeminiSessionScanner.ExtractSubagentInvocations(ModelDoneLine));
        Assert.Empty(GeminiSessionScanner.ExtractSubagentInvocations("壊れた行"));
    }

    // ===== Role とメンバーの完全一致照合 =====

    [Theory]
    [InlineData("visual-designer")]
    [InlineData("ビジュアル担当 (エガク)")]
    [InlineData("エガク")]
    [InlineData("  ビジュアル担当（エガク）  ")]
    [InlineData("ビジュアル担当(エガク)")]
    [InlineData("VISUAL-DESIGNER")]
    [InlineData("画像担当 (エガク)")]
    public void Matcher_名前_表示名_呼び名のいずれかと完全一致すればメンバーを特定する(string role)
    {
        Assert.Equal("visual-designer", SubagentRoleMatcher.Match(role, Members));
    }

    [Theory]
    [InlineData("エガクと相談しながら画像を作る担当")]
    [InlineData("ビジュアル担当")]
    [InlineData("エガク2号")]
    [InlineData("QA・いじわるテスト(Gemini版)")]
    [InlineData("")]
    [InlineData(null)]
    public void Matcher_説明文に名前が含まれるだけや不一致の場合は特定しない(string? role)
    {
        Assert.Null(SubagentRoleMatcher.Match(role, Members));
    }

    [Fact]
    public void Matcher_複数メンバーに一致する場合は特定しない()
    {
        PromptNominationCandidate[] dup = [new("a", "担当A (エガク)"), new("b", "担当B (エガク)")];

        Assert.Null(SubagentRoleMatcher.Match("エガク", dup));
    }

    /// <summary>かっこ外照合の確認用メンバー(リサーチ担当が2人いる構成)。</summary>
    private static readonly PromptNominationCandidate[] MembersWithResearchers =
    [
        new("tech-researcher", "リサーチ担当 (サガス)"),
        new("visual-designer", "ビジュアル担当 (エガク)"),
        new("market-researcher", "リサーチ担当 (シラベル)"),
    ];

    [Theory]
    [InlineData("サガス（リサーチ担当）", "tech-researcher")]
    [InlineData("サガス (リサーチ担当)", "tech-researcher")]
    [InlineData("  サガス(リサーチ担当)  ", "tech-researcher")]
    [InlineData("ビジュアル担当 (エガク)", "visual-designer")]
    [InlineData("エガク（画像担当）", "visual-designer")]
    public void Matcher_Roleのかっこ外の呼び名でもメンバーを特定する(string role, string expected)
    {
        Assert.Equal(expected, SubagentRoleMatcher.Match(role, MembersWithResearchers));
    }

    [Theory]
    [InlineData("リサーチ担当")]
    [InlineData("リサーチ担当（調査）")]
    [InlineData("サガスさん（リサーチ担当）")]
    public void Matcher_かっこ外が役割名だけや呼び名と完全一致しない場合は特定しない(string role)
    {
        Assert.Null(SubagentRoleMatcher.Match(role, MembersWithResearchers));
    }

    [Fact]
    public void Matcher_かっこ外の役割名が複数メンバーに一致する場合は特定しない()
    {
        // 表示名がかっこ無しの役割名だけのメンバーが2人いると、かっこ外「リサーチ担当」が両方に一致する
        PromptNominationCandidate[] dup = [new("r1", "リサーチ担当"), new("r2", "リサーチ担当")];

        Assert.Null(SubagentRoleMatcher.Match("リサーチ担当（調査）", dup));
    }

    [Fact]
    public void Matcher_かっこ外の役割名が1人だけの表示名と一致すればそのメンバーを特定する()
    {
        PromptNominationCandidate[] single = [new("r1", "リサーチ担当"), new("v1", "ビジュアル担当 (エガク)")];

        Assert.Equal("r1", SubagentRoleMatcher.Match("リサーチ担当（調査）", single));
    }

    // ===== Monitor: Running→Done の遷移 =====

    /// <summary>Gemini走査結果を差し替えられる監視インスタンスを作る。</summary>
    /// <param name="sessions">GeminiScanner が返すセッション一覧(呼び出しのたびに参照)。</param>
    private static MemberActivityMonitor CreateMonitor(Func<List<GeminiSessionRecord>> sessions) =>
        new()
        {
            NominationCandidatesProvider = () => Members,
            GeminiScanner = _ => sessions(),
        };

    /// <summary>invoke_subagent 1件を含むGeminiセッションの走査結果を作る。</summary>
    private static GeminiSessionRecord Session(MemberActivityState state, string typeName, string? role) =>
        new("conv-1", @"C:\Fake", state, null, DateTime.Now, [typeName], [new GeminiSubagentInvocation(typeName, role)]);

    [Fact]
    public void Monitor_TypeNameがselfでもRoleで特定したメンバーがRunningからDoneになる()
    {
        var current = new List<GeminiSessionRecord> { Session(MemberActivityState.Running, "self", "ビジュアル担当 (エガク)") };
        var monitor = CreateMonitor(() => current);

        monitor.PollTrackedSessions();
        Assert.Equal(MemberActivityState.Running, monitor.GetState("visual-designer"));

        current = [Session(MemberActivityState.Done, "self", "ビジュアル担当 (エガク)")];
        monitor.PollTrackedSessions();
        Assert.Equal(MemberActivityState.Done, monitor.GetState("visual-designer"));
    }

    [Fact]
    public void Monitor_TypeNameがメンバー名なら従来どおりそのメンバーがRunningになる()
    {
        var monitor = CreateMonitor(() => [Session(MemberActivityState.Running, "tsutae", "ドキュメント・整理担当（ツタエ）")]);

        monitor.PollTrackedSessions();

        Assert.Equal(MemberActivityState.Running, monitor.GetState("tsutae"));
    }

    [Fact]
    public void Monitor_Roleが説明文で名前を含むだけなら誰も光らない()
    {
        var monitor = CreateMonitor(() => [Session(MemberActivityState.Running, "self", "エガクの絵を参考にレビューする係")]);

        monitor.PollTrackedSessions();

        Assert.Equal(MemberActivityState.Idle, monitor.GetState("visual-designer"));
        Assert.Equal(MemberActivityState.Idle, monitor.GetState("shirabe"));
    }

    [Fact]
    public void Monitor_Roleなしのselfでは誰も光らない()
    {
        var monitor = CreateMonitor(() => [Session(MemberActivityState.Running, "self", null)]);

        monitor.PollTrackedSessions();

        Assert.All(Members, m => Assert.Equal(MemberActivityState.Idle, monitor.GetState(m.Id)));
    }

    [Fact]
    public void Monitor_Running中のセッションが走査結果から消えてもDoneに戻る()
    {
        var current = new List<GeminiSessionRecord> { Session(MemberActivityState.Running, "self", "エガク") };
        var monitor = CreateMonitor(() => current);

        monitor.PollTrackedSessions();
        Assert.Equal(MemberActivityState.Running, monitor.GetState("visual-designer"));

        current = [];
        monitor.PollTrackedSessions();
        Assert.Equal(MemberActivityState.Done, monitor.GetState("visual-designer"));
    }

    // ===== 実ファイルを使った通し確認(Scanner→Monitor) =====

    [Fact]
    public void 通し_transcriptにself呼び出しが追記されるとRunning_応答完了でDoneになる()
    {
        // 実際の ~/.gemini を読まないよう、一時フォルダの brain を直接走査させる
        var brainDir = Path.Combine(Path.GetTempPath(), "gemini-role-test-" + Guid.NewGuid());
        try
        {
            var logDir = Path.Combine(brainDir, Guid.NewGuid().ToString(), ".system_generated", "logs");
            Directory.CreateDirectory(logDir);
            var transcript = Path.Combine(logDir, "transcript.jsonl");
            File.WriteAllText(transcript, InvokeLine(Subagents("self", "ビジュアル担当 (エガク)"), status: "RUNNING") + "\n");

            var monitor = new MemberActivityMonitor
            {
                NominationCandidatesProvider = () => Members,
                GeminiScanner = minutes => GeminiSessionScanner.ScanActiveSessions(minutes, brainDir),
            };

            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("visual-designer"));

            File.AppendAllText(transcript, ModelDoneLine + "\n");
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("visual-designer"));
        }
        finally
        {
            Directory.Delete(brainDir, recursive: true);
        }
    }
}
