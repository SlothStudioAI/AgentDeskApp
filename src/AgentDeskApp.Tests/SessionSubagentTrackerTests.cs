using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="SessionSubagentTracker"/>(会話ログの増分読み込みによるサブエージェント稼働検知)に関するテスト。
/// 実機で確認した実際の行フォーマットを元にしたサンプルを使う。
/// </summary>
public class SessionSubagentTrackerTests
{
    private static string ToolUseLine(string toolUseId, string subagentName, bool runInBackground = false) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + toolUseId +
        "\",\"name\":\"Agent\",\"input\":{\"subagent_type\":\"" + subagentName +
        "\",\"description\":\"x\",\"prompt\":\"y\",\"run_in_background\":" + (runInBackground ? "true" : "false") + "}}]}}";

    private static string ToolResultLine(string toolUseId) =>
        "{\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + toolUseId +
        "\",\"content\":\"done\"}]}}";

    private static string TaskNotificationCompletedLine(string toolUseId) =>
        "{\"message\":{\"role\":\"user\",\"content\":\"<task-notification>\\n<tool-use-id>" + toolUseId +
        "</tool-use-id>\\n<status>completed</status>\\n</task-notification>\"}}";

    [Fact]
    public void Poll_tool_useのみだと稼働中扱いになる()
    {
        var path = CreateTempFile(ToolUseLine("toolu_1", "researcher") + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);

            Assert.Contains("researcher", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_tool_resultが来ると稼働中から外れる()
    {
        var path = CreateTempFile(ToolUseLine("toolu_1", "researcher") + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);
            Assert.Contains("researcher", tracker.RunningSubagentNames);

            File.AppendAllText(path, ToolResultLine("toolu_1") + "\n");
            tracker.Poll(path);

            Assert.DoesNotContain("researcher", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_複数回の追記で正しく増分読み込みされる()
    {
        var path = CreateTempFile(ToolUseLine("toolu_1", "researcher") + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);
            Assert.Single(tracker.RunningSubagentNames);

            File.AppendAllText(path, ToolUseLine("toolu_2", "coder") + "\n");
            tracker.Poll(path);

            Assert.Equal(2, tracker.RunningSubagentNames.Count);
            Assert.Contains("researcher", tracker.RunningSubagentNames);
            Assert.Contains("coder", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_改行で終わっていない末尾の行は次回まで処理しない()
    {
        var path = CreateTempFile(ToolUseLine("toolu_1", "researcher")); // 改行なし
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);

            Assert.Empty(tracker.RunningSubagentNames);

            File.AppendAllText(path, "\n");
            tracker.Poll(path);

            Assert.Contains("researcher", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_Task名義の呼び出しも稼働中として検知する()
    {
        var line = """
            {"message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_1","name":"Task","input":{"subagent_type":"researcher"}}]}}
            """.ReplaceLineEndings("");
        var path = CreateTempFile(line + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);

            // U-25: 外部CLI/メインチャットセッション経由では"Task"ツール名義で呼ばれるケースがあるため検知できること。
            Assert.Contains("researcher", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_ファイルが無ければ何もしない()
    {
        var tracker = new SessionSubagentTracker();
        tracker.Poll(Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid() + ".jsonl"));

        Assert.Empty(tracker.RunningSubagentNames);
    }

    [Fact]
    public void Poll_Agent以外のtool_useは無視する()
    {
        var line = """
            {"message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_1","name":"WebSearch","input":{"query":"x"}}]}}
            """.ReplaceLineEndings("");
        var path = CreateTempFile(line + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);

            Assert.Empty(tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_バックグラウンド呼び出しはtool_resultだけでは稼働中から外れない()
    {
        var path = CreateTempFile(ToolUseLine("toolu_1", "research", runInBackground: true) + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);
            Assert.Contains("research", tracker.RunningSubagentNames);

            // バックグラウンド呼び出しの即時tool_result(起動受付の合図)。
            File.AppendAllText(path, ToolResultLine("toolu_1") + "\n");
            tracker.Poll(path);

            Assert.Contains("research", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_バックグラウンド呼び出しはtask_notification完了で稼働中から外れる()
    {
        var path = CreateTempFile(
            ToolUseLine("toolu_1", "research", runInBackground: true) + "\n" +
            ToolResultLine("toolu_1") + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);
            Assert.Contains("research", tracker.RunningSubagentNames);

            File.AppendAllText(path, TaskNotificationCompletedLine("toolu_1") + "\n");
            tracker.Poll(path);

            Assert.DoesNotContain("research", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ==== ここからイジワルQA: 異常系・境界値の追加テスト ====

    [Fact]
    public void Poll_壊れたJSON行が混在しても後続の正常な行は処理される()
    {
        var path = CreateTempFile(
            "{not valid json\n" +
            ToolUseLine("toolu_1", "researcher") + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);

            Assert.Contains("researcher", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_ファイル縮小検知時は稼働中の状態を破棄する()
    {
        var path = CreateTempFile(ToolUseLine("toolu_1", "researcher") + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            tracker.Poll(path);
            Assert.Contains("researcher", tracker.RunningSubagentNames);

            // ファイルがローテーション等で縮小・再作成された状況を再現
            File.WriteAllText(path, ToolUseLine("toolu_2", "coder") + "\n");
            tracker.Poll(path);

            // 旧セッションの稼働中状態(researcher)は破棄され、新しい呼び出し(coder)だけが残る
            Assert.DoesNotContain("researcher", tracker.RunningSubagentNames);
            Assert.Contains("coder", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_同一tool_use_idのtool_resultが二重に来ても例外にならない()
    {
        var path = CreateTempFile(
            ToolUseLine("toolu_1", "researcher") + "\n" +
            ToolResultLine("toolu_1") + "\n" +
            ToolResultLine("toolu_1") + "\n"); // 重複したtool_result
        try
        {
            var tracker = new SessionSubagentTracker();
            var ex = Record.Exception(() => tracker.Poll(path));

            Assert.Null(ex);
            Assert.DoesNotContain("researcher", tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_task_notification完了行に対応するtool_use_idが存在しなくても例外にならない()
    {
        // 未知のtool-use-idへのtask-notificationが届くケース(ログ欠落・順序前後を想定)。
        var path = CreateTempFile(TaskNotificationCompletedLine("unknown-toolu-999") + "\n");
        try
        {
            var tracker = new SessionSubagentTracker();
            var ex = Record.Exception(() => tracker.Poll(path));

            Assert.Null(ex);
            Assert.Empty(tracker.RunningSubagentNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "session-tracker-test-" + Guid.NewGuid() + ".jsonl");
        File.WriteAllText(path, content);
        return path;
    }
}
