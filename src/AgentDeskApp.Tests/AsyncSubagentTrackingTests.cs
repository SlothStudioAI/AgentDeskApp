using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// 非同期起動サブエージェント(Claudeデスクトップ: run_in_background無しでも async_launched で返る)の追跡テスト。
/// 実データの形(tool_use → async_launchedのtool_result → 親ターン終了 → task-notification)を再現する。
/// </summary>
public class AsyncSubagentTrackingTests
{
    private static string ToolUse(string id) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"name\":\"Agent\",\"id\":\"" + id +
        "\",\"input\":{\"description\":\"担当: ツクル 検知テスト\",\"prompt\":\"担当: ツクル（実装担当）\"}}]}}";

    private static string AsyncLaunchedResult(string id) =>
        "{\"toolUseResult\":{\"isAsync\":true,\"status\":\"async_launched\",\"agentId\":\"a61c0e\"}," +
        "\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id +
        "\",\"content\":\"Async agent launched successfully.\"}]}}";

    private static string SyncResult(string id) =>
        "{\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id +
        "\",\"content\":\"finished\"}]}}";

    private static string Notification(string id, string status = "completed") =>
        "{\"message\":{\"role\":\"user\",\"content\":\"<task-notification>\\n<tool-use-id>" + id +
        "</tool-use-id>\\n<status>" + status + "</status>\\n</task-notification>\"}}";

    private static readonly PromptNominationCandidate[] Candidates = [new("ツクル", "ツクル")];

    private static SessionSubagentTracker NewTracker(Func<DateTime>? clock = null) => new()
    {
        NominationCandidatesProvider = () => Candidates,
        Clock = clock ?? (() => DateTime.Now),
    };

    private static string Temp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "async-sub-" + Guid.NewGuid() + ".jsonl");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void async_launched後も作業中が残りtask_notificationで消える()
    {
        var path = Temp(ToolUse("t1") + "\n");
        try
        {
            var tracker = NewTracker();
            tracker.Poll(path);
            Assert.Contains("ツクル", tracker.RunningSubagentNames);

            File.AppendAllText(path, AsyncLaunchedResult("t1") + "\n");
            tracker.Poll(path);
            Assert.Contains("ツクル", tracker.RunningSubagentNames);
            Assert.True(tracker.HasPendingAsync);

            File.AppendAllText(path, Notification("t1") + "\n");
            tracker.Poll(path);
            Assert.DoesNotContain("ツクル", tracker.RunningSubagentNames);
            Assert.False(tracker.HasPendingAsync);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 本文のAsync_agent_launchedだけでも非同期扱いになる()
    {
        var line = "{\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"Async agent launched successfully\"}]}}";
        var path = Temp(ToolUse("t1") + "\n" + line + "\n");
        try
        {
            var tracker = NewTracker();
            tracker.Poll(path);
            Assert.Contains("ツクル", tracker.RunningSubagentNames);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 同期型はtool_resultで従来どおり消える()
    {
        var path = Temp(ToolUse("t1") + "\n" + SyncResult("t1") + "\n");
        try
        {
            var tracker = NewTracker();
            tracker.Poll(path);
            Assert.Empty(tracker.RunningSubagentNames);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void failedやkilledの通知でも終了扱いになる()
    {
        foreach (var status in new[] { "failed", "killed" })
        {
            var path = Temp(ToolUse("t1") + "\n" + AsyncLaunchedResult("t1") + "\n" + Notification("t1", status) + "\n");
            try
            {
                var tracker = NewTracker();
                tracker.Poll(path);
                Assert.Empty(tracker.RunningSubagentNames);
            }
            finally { File.Delete(path); }
        }
    }

    [Fact]
    public void 最大追跡時間を超えると安全弁で打ち切られる()
    {
        var now = new DateTime(2026, 10, 4, 12, 0, 0);
        var path = Temp(ToolUse("t1") + "\n" + AsyncLaunchedResult("t1") + "\n");
        try
        {
            var tracker = NewTracker(() => now);
            tracker.AsyncMaxTrackDuration = TimeSpan.FromMinutes(30);
            tracker.Poll(path);
            Assert.Contains("ツクル", tracker.RunningSubagentNames);

            now = now.AddMinutes(29);
            tracker.Poll(path);
            Assert.Contains("ツクル", tracker.RunningSubagentNames);

            now = now.AddMinutes(2);
            tracker.Poll(path);
            Assert.Empty(tracker.RunningSubagentNames);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 親がidleになってもpendingがある間はtrackerを保持し完了通知でDoneになる()
    {
        var home = Path.Combine(Path.GetTempPath(), "async-mon-" + Guid.NewGuid());
        Directory.CreateDirectory(home);
        var original = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", home);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Project";
            var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ToolUse("t1") + "\n" + AsyncLaunchedResult("t1") + "\n");

            var now = new DateTime(2026, 10, 4, 12, 0, 0);
            var monitor = new MemberActivityMonitor
            {
                Clock = () => now,
                NominationCandidatesProvider = () => Candidates,
                AsyncSubagentMaxTrackMinutes = 30,
            };
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "interactive", "busy", null, "t")]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            // 親がidleになっても(一覧から消えても)追跡を続ける
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "interactive", "idle", null, "t")]);
            monitor.PollTrackedSessions();
            monitor.RefreshBusySessions([]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            // 完了通知 → Done、その後の更新でtrackerも破棄
            File.AppendAllText(path, Notification("t1") + "\n");
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
            monitor.RefreshBusySessions([]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", original);
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void 完了通知が来ないまま最大時間を超えたらDoneになる()
    {
        var home = Path.Combine(Path.GetTempPath(), "async-mon-" + Guid.NewGuid());
        Directory.CreateDirectory(home);
        var original = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", home);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Project";
            var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ToolUse("t1") + "\n" + AsyncLaunchedResult("t1") + "\n");

            var now = new DateTime(2026, 10, 4, 12, 0, 0);
            var monitor = new MemberActivityMonitor
            {
                Clock = () => now,
                NominationCandidatesProvider = () => Candidates,
                AsyncSubagentMaxTrackMinutes = 30,
            };
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "interactive", "busy", null, "t")]);
            monitor.PollTrackedSessions();

            now = now.AddMinutes(31);
            monitor.RefreshBusySessions([]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", original);
            Directory.Delete(home, recursive: true);
        }
    }
}
