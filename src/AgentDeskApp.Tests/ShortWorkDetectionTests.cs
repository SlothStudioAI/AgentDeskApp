using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// 実機で「Poll周期より短い作業(tool_useと完了通知が同じPollで読まれる)」が取りこぼされた問題の回帰テスト。
/// Runningを観測できなくてもDoneへ遷移すること、最短表示時間まではRunningを保つことを確認する。
/// </summary>
[Collection("UserProfileEnv")]
public class ShortWorkDetectionTests
{
    private static string ToolUse(string id, bool background = false) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"name\":\"Agent\",\"id\":\"" + id +
        "\",\"input\":{" + (background ? "\"run_in_background\":true," : "") +
        "\"description\":\"担当: ツクル 検知テスト\",\"prompt\":\"担当: ツクル（実装担当）秘密の本文SECRET-PROMPT\"}}]}}";

    private static string AsyncLaunchedResult(string id) =>
        "{\"toolUseResult\":{\"isAsync\":true,\"status\":\"async_launched\"}," +
        "\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id +
        "\",\"content\":\"Async agent launched successfully.\"}]}}";

    private static string Notification(string id) =>
        "{\"message\":{\"role\":\"user\",\"content\":\"<task-notification>\\n<tool-use-id>" + id +
        "</tool-use-id>\\n<status>completed</status>\\n</task-notification>\"}}";

    private static readonly PromptNominationCandidate[] Candidates = [new("ツクル", "ツクル")];

    private static AgentSessionInfo Session(string id, string cwd, string status = "idle") =>
        new(id, cwd, "interactive", status, null, "t");

    /// <summary>疑似ホームと会話ログを用意し、初回発見(idle→末尾から追跡)済みのモニターで処理を実行する。</summary>
    private static void Run(int minRunningSeconds, Action<MemberActivityMonitor, string, Action<DateTime>> body, DiagnosticLog? log = null)
    {
        var home = Path.Combine(Path.GetTempPath(), "shortwork-" + Guid.NewGuid());
        Directory.CreateDirectory(home);
        var original = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", home);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\ShortWork";
            var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");

            var now = new DateTime(2026, 10, 4, 2, 20, 0);
            var monitor = new MemberActivityMonitor
            {
                Clock = () => now,
                NominationCandidatesProvider = () => Candidates,
                MinRunningDisplaySeconds = minRunningSeconds,
            };
            if (log is not null)
            {
                monitor.Log = log;
            }

            monitor.RefreshBusySessions([Session(sessionId, cwd)]);
            monitor.PollTrackedSessions();
            body(monitor, path, t => now = t);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", original);
            try { Directory.Delete(home, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 一回のPollでtool_useと完了通知が両方読まれてもDoneになる()
    {
        Run(0, (monitor, path, setNow) =>
        {
            File.AppendAllText(path, ToolUse("t1", background: true) + "\n" + AsyncLaunchedResult("t1") + "\n" + Notification("t1") + "\n");

            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void 同期型のtool_useとtool_resultが同じPollで読まれてもDoneになる()
    {
        Run(0, (monitor, path, setNow) =>
        {
            var result = "{\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"ok\"}]}}";
            File.AppendAllText(path, ToolUse("t1") + "\n" + result + "\n");

            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void 最短表示時間までは完了通知が先に来てもRunningを保ちその後Doneになる()
    {
        Run(3, (monitor, path, setNow) =>
        {
            var start = new DateTime(2026, 10, 4, 2, 21, 0);
            setNow(start);
            File.AppendAllText(path, ToolUse("t1", background: true) + "\n" + AsyncLaunchedResult("t1") + "\n" + Notification("t1") + "\n");

            // 同じPollで両方読まれた → Running表示を始める(画面側で揺れが見える)
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            // 最短秒数(3秒)の手前まではRunningのまま
            setNow(start.AddSeconds(2.9));
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            // 最短秒数を過ぎたらDone
            setNow(start.AddSeconds(3));
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void Runningを観測済みで完了通知が先に来た場合も最短表示時間までRunningを保つ()
    {
        Run(3, (monitor, path, setNow) =>
        {
            var start = new DateTime(2026, 10, 4, 2, 21, 0);
            setNow(start);
            File.AppendAllText(path, ToolUse("t1", background: true) + "\n" + AsyncLaunchedResult("t1") + "\n");
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            // 1秒後に完了通知。まだ最短表示時間内なのでRunningのまま
            setNow(start.AddSeconds(1));
            File.AppendAllText(path, Notification("t1") + "\n");
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));

            setNow(start.AddSeconds(4));
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void 長時間作業は完了通知の次のPollですぐDoneになる()
    {
        Run(3, (monitor, path, setNow) =>
        {
            var start = new DateTime(2026, 10, 4, 2, 21, 0);
            setNow(start);
            File.AppendAllText(path, ToolUse("t1", background: true) + "\n" + AsyncLaunchedResult("t1") + "\n");
            monitor.PollTrackedSessions();

            setNow(start.AddMinutes(5));
            File.AppendAllText(path, Notification("t1") + "\n");
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Done, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void 完了通知が来ない限りRunningのままで最短表示時間でDoneにならない()
    {
        Run(3, (monitor, path, setNow) =>
        {
            var start = new DateTime(2026, 10, 4, 2, 21, 0);
            setNow(start);
            File.AppendAllText(path, ToolUse("t1", background: true) + "\n" + AsyncLaunchedResult("t1") + "\n");
            monitor.PollTrackedSessions();

            setNow(start.AddSeconds(60));
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("ツクル"));
        });
    }

    [Fact]
    public void トラッカーは前回Poll以降に完了した名前を取り出して空にする()
    {
        var home = Path.Combine(Path.GetTempPath(), "shortwork-tr-" + Guid.NewGuid());
        Directory.CreateDirectory(home);
        try
        {
            var path = Path.Combine(home, "t.jsonl");
            File.WriteAllText(path, ToolUse("t1", background: true) + "\n" + Notification("t1") + "\n");
            var tracker = new SessionSubagentTracker { NominationCandidatesProvider = () => Candidates };

            tracker.Poll(path);

            Assert.Empty(tracker.RunningSubagentNames);
            Assert.Equal(["ツクル"], tracker.ConsumeCompletedNames());
            Assert.Empty(tracker.ConsumeCompletedNames());
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void 診断ログに状態遷移と読了行数が出てプロンプト本文やパスは出ない()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "shortwork-log-" + Guid.NewGuid());
        var logPath = Path.Combine(logDir, "logs", "d.log");
        try
        {
            var log = new DiagnosticLog(logPath, enabled: true, maxBytes: 1_000_000);
            Run(3, (monitor, path, setNow) =>
            {
                File.AppendAllText(path, ToolUse("t1") + "\n" + AsyncLaunchedResult("t1") + "\n" + Notification("t1") + "\n");
                monitor.PollTrackedSessions();
                setNow(new DateTime(2026, 10, 4, 2, 30, 0));
                monitor.PollTrackedSessions();
            }, log);

            var text = File.ReadAllText(logPath);
            Assert.Contains("agents取得 total=1 interactive=1 [idle=1] background=0", text);
            Assert.Contains("読了行数", text);
            Assert.Contains("tool_use検知", text);
            Assert.Contains("async_launched確認", text);
            Assert.Contains("task-notification完了", text);
            Assert.Contains("メンバー状態 name=ツクル Idle->Running", text);
            Assert.Contains("メンバー状態 name=ツクル Running->Done", text);
            Assert.DoesNotContain("SECRET-PROMPT", text);
            Assert.DoesNotContain("Fake", text);
            Assert.DoesNotContain("shortwork", text);
            Assert.DoesNotContain("実装担当", text);
        }
        finally
        {
            try { Directory.Delete(logDir, true); } catch (IOException) { }
        }
    }
}
