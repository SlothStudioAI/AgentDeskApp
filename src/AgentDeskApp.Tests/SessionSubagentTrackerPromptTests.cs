using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="SessionSubagentTracker"/>のAgent/Task命令文(prompt/description)からのメンバー名検出テスト(BUG-8)。
/// </summary>
public class SessionSubagentTrackerPromptTests
{
    private static readonly PromptNominationCandidate[] Members =
    [
        new("tsukuru", "ツクル"),
        new("shirabe", "シラベ"),
        new("ijiwaru", "QA担当 (イジワル)"),
    ];

    /// <summary>Agent tool_useの1行(会話ログ形式)を作る。</summary>
    private static string ToolUseLine(string toolUseId, string subagentType, string prompt, bool background = false, string description = "作業") =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + toolUseId +
        "\",\"name\":\"Agent\",\"input\":{\"subagent_type\":\"" + subagentType + "\",\"description\":\"" + description +
        "\",\"prompt\":\"" + prompt + "\",\"run_in_background\":" + (background ? "true" : "false") + "}}]}}";

    private static string ToolResultLine(string toolUseId) =>
        "{\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + toolUseId + "\",\"content\":\"ok\"}]}}";

    private static string TaskNotificationCompletedLine(string toolUseId) =>
        "{\"message\":{\"role\":\"user\",\"content\":\"<task-notification>\\n<tool-use-id>" + toolUseId +
        "</tool-use-id>\\n<status>completed</status>\\n</task-notification>\"}}";

    /// <summary>空の会話ログと、候補一覧付き(または無し)のトラッカーを用意する。</summary>
    private static (SessionSubagentTracker Tracker, string Path) CreateTracker(bool withCandidates = true)
    {
        var path = Path.Combine(Path.GetTempPath(), "tracker-instr-" + Guid.NewGuid() + ".jsonl");
        File.WriteAllText(path, string.Empty);
        var tracker = new SessionSubagentTracker { NominationCandidatesProvider = withCandidates ? () => Members : null };
        return (tracker, path);
    }

    /// <summary>1行追記してPollし、その時点の稼働中名を返す。</summary>
    private static IReadOnlySet<string> Append(SessionSubagentTracker tracker, string path, string line)
    {
        File.AppendAllText(path, line + "\n");
        tracker.Poll(path);
        return tracker.RunningSubagentNames;
    }

    [Fact]
    public void 汎用エージェントの命令文の役割宣言でメンバーが稼働中になりtool_resultで外れる()
    {
        var (tracker, path) = CreateTracker();
        try
        {
            var running = Append(tracker, path, ToolUseLine("t1", "general-purpose", "あなたは開発チームのソフトウェアエンジニア『ツクル』です。実装してください"));
            Assert.Contains("tsukuru", running);
            Assert.DoesNotContain("shirabe", running);

            Assert.DoesNotContain("tsukuru", Append(tracker, path, ToolResultLine("t1")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void バックグラウンド呼び出しはtool_resultでは外れずtask_notificationで外れる()
    {
        var (tracker, path) = CreateTracker();
        try
        {
            Append(tracker, path, ToolUseLine("t1", "general-purpose", "ツクルとして実装してください", background: true));
            Assert.Contains("tsukuru", Append(tracker, path, ToolResultLine("t1")));
            Assert.DoesNotContain("tsukuru", Append(tracker, path, TaskNotificationCompletedLine("t1")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void subagent_typeが既知メンバーIdなら命令文の他の名前は拾わず従来どおり動く()
    {
        var (tracker, path) = CreateTracker();
        try
        {
            var running = Append(tracker, path, ToolUseLine("t1", "tsukuru", "シラベに確認して"));
            Assert.Contains("tsukuru", running);
            Assert.DoesNotContain("shirabe", running);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 単なる言及だけの命令文は拾わない()
    {
        var (tracker, path) = CreateTracker();
        try
        {
            var running = Append(tracker, path, ToolUseLine("t1", "general-purpose", "ツクルの設計を参考に調査してください"));
            Assert.DoesNotContain("tsukuru", running);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 複数のメンバー名は全員が対象になる()
    {
        var (tracker, path) = CreateTracker();
        try
        {
            var running = Append(tracker, path, ToolUseLine("t1", "general-purpose", "担当: ツクル\\n担当: シラベ"));
            Assert.Contains("tsukuru", running);
            Assert.Contains("shirabe", running);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 命令文にメンバー名が無ければメンバーは稼働中にならない()
    {
        var (tracker, path) = CreateTracker();
        try
        {
            var running = Append(tracker, path, ToolUseLine("t1", "general-purpose", "コードを調べてください"));
            Assert.DoesNotContain("tsukuru", running);
            Assert.DoesNotContain("shirabe", running);
            Assert.DoesNotContain("ijiwaru", running);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 表示名の括弧内の呼び名で検出しIdで返る()
    {
        var (tracker, path) = CreateTracker();
        try
        {
            var running = Append(tracker, path, ToolUseLine("t1", "general-purpose", "レビューをお願いします", description: "イジワルに確認して"));
            Assert.Contains("ijiwaru", running);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 候補一覧が未設定なら命令文の検出は行わない()
    {
        var (tracker, path) = CreateTracker(withCandidates: false);
        try
        {
            var running = Append(tracker, path, ToolUseLine("t1", "general-purpose", "あなたは『ツクル』です"));
            Assert.DoesNotContain("tsukuru", running);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 名前が一致しないAgent呼び出しは診断ログに名前未一致として出る_本文は出さない()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "tracker-diag-" + Guid.NewGuid() + ".log");
        var (tracker, path) = CreateTracker();
        try
        {
            tracker.Log = new DiagnosticLog(logPath, true, 100000);
            Append(tracker, path, ToolUseLine("t1", "", "秘密の本文ですよ", description: "調査のお願いの長い説明文"));

            var text = File.ReadAllText(logPath);
            Assert.Contains("Agent呼び出し検出 名前未一致 candidates=3 subagent_type=", text);
            Assert.DoesNotContain("descriptionHead", text);
            Assert.DoesNotContain("調査のお願い", text);
            Assert.DoesNotContain("説明文", text);
            Assert.DoesNotContain("秘密の本文", text);
        }
        finally { File.Delete(path); File.Delete(logPath); }
    }

    [Fact]
    public void 候補一覧が空のときの警告は1回だけ出る()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "tracker-diag-" + Guid.NewGuid() + ".log");
        var (tracker, path) = CreateTracker(withCandidates: false);
        try
        {
            tracker.Log = new DiagnosticLog(logPath, true, 100000);
            Append(tracker, path, ToolUseLine("t1", "general-purpose", "実装して"));
            Append(tracker, path, ToolUseLine("t2", "general-purpose", "実装して"));

            var warnings = File.ReadAllLines(logPath).Count(l => l.Contains("警告 候補一覧が"));
            Assert.Equal(1, warnings);
        }
        finally { File.Delete(path); File.Delete(logPath); }
    }
}
