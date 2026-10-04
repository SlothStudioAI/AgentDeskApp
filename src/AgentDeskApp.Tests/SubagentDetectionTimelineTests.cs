using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// 実機で「親セッションが一瞬しかbusyにならず、更新周期に当たらないためサブエージェントを検知できない」
/// 問題の回帰テスト。agents一覧(スタブ)・時刻・会話ログの追記を注入し、実周期どおりRefresh/Pollを呼ぶ。
/// </summary>
[Collection("UserProfileEnv")]
public class SubagentDetectionTimelineTests
{
    private static string ToolUse(string id) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"name\":\"Agent\",\"id\":\"" + id +
        "\",\"input\":{\"description\":\"担当: ツクル 実装\",\"prompt\":\"担当: ツクル（実装担当）\"}}]}}";

    private static string AsyncLaunchedResult(string id) =>
        "{\"toolUseResult\":{\"isAsync\":true,\"status\":\"async_launched\"}," +
        "\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id +
        "\",\"content\":\"Async agent launched successfully.\"}]}}";

    private static string Notification(string id) =>
        "{\"message\":{\"role\":\"user\",\"content\":\"<task-notification>\\n<tool-use-id>" + id +
        "</tool-use-id>\\n<status>completed</status>\\n</task-notification>\"}}";

    private static readonly PromptNominationCandidate[] Candidates = [new("ツクル", "ツクル")];

    /// <summary>テスト用の疑似ホームを用意して処理を実行する。</summary>
    private static void WithFakeHome(Action<string> body)
    {
        var home = Path.Combine(Path.GetTempPath(), "timeline-" + Guid.NewGuid());
        Directory.CreateDirectory(home);
        var original = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", home);
        try { body(home); }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", original);
            try { Directory.Delete(home, true); } catch (IOException) { }
        }
    }

    private static AgentSessionInfo Session(string id, string cwd, string status) =>
        new(id, cwd, "interactive", status, null, "t");

    [Theory]
    [InlineData(false)] // 親はずっとidle
    [InlineData(true)]  // 親は一瞬busyだが、更新周期には当たらない(Refreshの間に収まる)
    public void 親が更新周期に当たらなくても追記された非同期呼び出しを検知しDoneまで追える(bool parentBriefBusy)
    {
        WithFakeHome(_ =>
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Timeline";
            var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 発見前の古い行: 古い非同期呼び出しと、その完了通知されていない呼び出し(誤検知してはならない)
            File.WriteAllText(path, ToolUse("old1") + "\n" + AsyncLaunchedResult("old1") + "\n");

            var now = new DateTime(2026, 10, 4, 2, 0, 0);
            var monitor = new MemberActivityMonitor { Clock = () => now, NominationCandidatesProvider = () => Candidates };

            // 初回発見(idle): 過去の行では検知しない
            monitor.RefreshBusySessions([Session(sessionId, cwd, "idle")]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Idle, monitor.GetState("ツクル"));

            // 親が一瞬busyになり、tool_useとasync_launchedが書かれ、Refreshの前にidleへ戻る
            File.AppendAllText(path, ToolUse("t1") + "\n");
            now = now.AddSeconds(2);
            File.AppendAllText(path, AsyncLaunchedResult("t1") + "\n");
            now = now.AddSeconds(5);
            monitor.PollTrackedSessions(); // Poll周期(5秒)
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            // 次のRefresh時点では親はidle(またはbusy)。追跡は途切れない
            now = now.AddSeconds(10);
            monitor.RefreshBusySessions([Session(sessionId, cwd, parentBriefBusy ? "busy" : "idle")]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            // 30秒後に完了通知 → Done
            now = now.AddSeconds(30);
            File.AppendAllText(path, Notification("t1") + "\n");
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void 初回発見前の完了待ち呼び出しは追わず新しい呼び出しだけ検知する()
    {
        WithFakeHome(_ =>
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Timeline2";
            var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ToolUse("old1") + "\n" + AsyncLaunchedResult("old1") + "\n");

            var monitor = new MemberActivityMonitor { NominationCandidatesProvider = () => Candidates };
            monitor.RefreshBusySessions([Session(sessionId, cwd, "idle")]);
            File.AppendAllText(path, Notification("old1") + "\n"); // 古い呼び出しの完了通知が後から来ても害は無い
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Idle, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void agents一覧から消えてpendingも無ければ追跡を破棄する()
    {
        WithFakeHome(_ =>
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Timeline3";
            var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");

            var monitor = new MemberActivityMonitor { NominationCandidatesProvider = () => Candidates };
            monitor.RefreshBusySessions([Session(sessionId, cwd, "idle")]);
            monitor.RefreshBusySessions([]);
            File.AppendAllText(path, ToolUse("t1") + "\n" + AsyncLaunchedResult("t1") + "\n");
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Idle, monitor.GetState("ツクル"));
        });
    }
}
