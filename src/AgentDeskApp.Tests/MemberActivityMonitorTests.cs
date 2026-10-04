using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="MemberActivityMonitor"/>(2段階ポーリングの統括ロジック)に関するテスト。
/// 実際のセッションログファイルを使い、Idle→Running→Doneの遷移を確認する。
/// </summary>
[Collection("UserProfileEnv")]
public class MemberActivityMonitorTests
{
    [Fact]
    public void 未追跡のメンバーはIdle()
    {
        var monitor = new MemberActivityMonitor();

        Assert.Equal(MemberActivityState.Idle, monitor.GetState("researcher"));
    }

    [Fact]
    public async Task 状態の書き込みと列挙が同時に走っても例外にならない()
    {
        // BUG-1: 書き込み中の辞書を列挙するとInvalidOperationExceptionになり、監視が止まっていた。
        var monitor = new MemberActivityMonitor();
        using var stop = new CancellationTokenSource();

        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 3000; i++)
            {
                monitor.ReportDirectLaunchOutcome($"member-{w}-{i}", MemberActivityState.Cancelled);
            }
        })).ToArray();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var _ in monitor.States)
                {
                }

                monitor.GetState("member-0-0");
                monitor.PollTrackedSessions();
            }
        });

        await Task.WhenAll(writers);
        stop.Cancel();
        await reader; // 例外が出ていればここで再スローされる

        Assert.Equal(MemberActivityState.Cancelled, monitor.GetState("member-3-2999"));
    }

    [Fact]
    public void 破棄済みProcessが登録されていてもPollとGetStateは例外にならずDoneになる()
    {
        // 実行ウィンドウがusingでProcessをDisposeした後もモニターが参照を持ち続けるケース。
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        var process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
        var monitor = new MemberActivityMonitor();
        monitor.RegisterDirectLaunch("worker", process);
        process.Dispose();

        monitor.PollTrackedSessions();

        Assert.Equal(MemberActivityState.Done, monitor.GetState("worker"));
    }

    [Fact]
    public void busyなセッションを追跡しRunningに遷移する()
    {
        var tempDir = CreateTempProjectsDir(out var restoreHome);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Project";
            var transcriptPath = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
            File.WriteAllText(transcriptPath, ToolUseLine("toolu_1", "researcher") + "\n");

            var monitor = new MemberActivityMonitor();
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "interactive", "busy", null, "test")]);
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Running, monitor.GetState("researcher"));
        }
        finally
        {
            restoreHome();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void セッションがidleに戻ると追跡をやめ直前の状態をDoneのまま保持する()
    {
        var tempDir = CreateTempProjectsDir(out var restoreHome);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Project";
            var transcriptPath = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
            File.WriteAllText(transcriptPath, ToolUseLine("toolu_1", "researcher") + "\n");

            var monitor = new MemberActivityMonitor();
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "interactive", "busy", null, "test")]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("researcher"));

            // セッションがidleに戻った(=busy一覧から消えた)
            monitor.RefreshBusySessions([]);
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Done, monitor.GetState("researcher"));
        }
        finally
        {
            restoreHome();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void セッションが無いフォルダはGetLeadersが空()
    {
        var monitor = new MemberActivityMonitor();

        Assert.Empty(monitor.GetLeaders(@"C:\Work"));
    }

    [Fact]
    public void 待機のままのセッションはGetLeadersに出ない()
    {
        var monitor = new MemberActivityMonitor();

        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "idle", null, "test")]);

        // 開けっぱなしの旧ウィンドウ等、一度もbusyになっていないセッションはカード化しない。
        Assert.Empty(monitor.GetLeaders(@"C:\Work"));
    }

    [Fact]
    public void 待機中でも会話ログが直近なら直近の待機会話として数える()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0);
        var monitor = new MemberActivityMonitor
        {
            Clock = () => now,
            ClaudeRecentIdleMinutes = 5,
            TranscriptLastWriteProvider = (_, id) => id == "recent" ? now.AddMinutes(-4) : now.AddMinutes(-6),
        };

        monitor.RefreshBusySessions([
            new AgentSessionInfo("recent", @"C:\Work", "interactive", "idle", null, "t"),
            new AgentSessionInfo("old", @"C:\Work", "interactive", "idle", null, "t"),
            new AgentSessionInfo("other", @"C:\Other", "interactive", "idle", null, "t"),
        ]);

        // 直近5分以内の更新があるものだけ。古い旧ウィンドウ・別フォルダは数えない。GetLeaders(作業中の数)には影響しない。
        Assert.Equal(["recent"], monitor.GetRecentIdleLeaderSessionIds(@"C:\Work"));
        Assert.Empty(monitor.GetLeaders(@"C:\Work"));
    }

    [Fact]
    public void 作業中のセッションは直近の待機会話に含めない()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0);
        var monitor = new MemberActivityMonitor { Clock = () => now, TranscriptLastWriteProvider = (_, _) => now };

        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "busy", null, "t")]);

        Assert.Empty(monitor.GetRecentIdleLeaderSessionIds(@"C:\Work"));
    }

    [Fact]
    public void Geminiの直近分数の既定は5分で状態検知用の180分とは別である()
    {
        var passed = 0;
        var monitor = new MemberActivityMonitor { GeminiScanner = s => { passed = s; return []; } };

        monitor.GetLeaders(@"C:\Work");

        Assert.Equal(5, passed);
        Assert.Equal(180, MemberActivityMonitor.GeminiMemberStateWindowMinutes);
    }

    [Fact]
    public void Geminiの直近分数は設定値が分としてスキャナーに渡される()
    {
        var passed = 0;
        var monitor = new MemberActivityMonitor { GeminiRecentMinutes = 240, GeminiScanner = s => { passed = s; return []; } };

        monitor.GetLeaders(@"C:\Work");

        Assert.Equal(240, passed);
    }

    [Fact]
    public void busyなセッションはGetLeadersに1件返る()
    {
        var monitor = new MemberActivityMonitor();

        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "busy", null, "test")]);

        // 末尾の区切り文字・大文字小文字の違いを無視して一致すること。
        Assert.Single(monitor.GetLeaders(@"C:\Work"));
        Assert.Single(monitor.GetLeaders(@"c:\work\"));
        Assert.Equal(MemberActivityState.Running, monitor.GetLeaders(@"C:\Work")[0].State);
    }

    [Fact]
    public void 同じフォルダに複数セッションがいてもbusyまたは完了直後のものだけGetLeadersに残る()
    {
        var monitor = new MemberActivityMonitor();

        monitor.RefreshBusySessions([
            new AgentSessionInfo("s1", @"C:\Work", "interactive", "busy", null, "test1"),
            new AgentSessionInfo("s2", @"C:\Work", "interactive", "idle", null, "test2"),
        ]);

        var leaders = monitor.GetLeaders(@"C:\Work");
        Assert.Single(leaders);
        Assert.Contains(leaders, l => l.SessionId == "s1" && l.State == MemberActivityState.Running);
    }

    [Fact]
    public void busyなセッションのフォルダはリーダー状態がRunningになりidleに戻るとDoneになる()
    {
        var monitor = new MemberActivityMonitor();

        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "busy", null, "test")]);
        Assert.Equal(MemberActivityState.Running, monitor.GetLeaders(@"C:\Work")[0].State);
        Assert.Equal(MemberActivityState.Running, monitor.GetLeaderSessionState("s1"));

        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "idle", null, "test")]);
        Assert.Equal(MemberActivityState.Done, monitor.GetLeaders(@"C:\Work")[0].State);
        Assert.Single(monitor.GetLeaders(@"C:\Work"));
    }

    [Fact]
    public void セッションが閉じられるとGetLeadersから消える()
    {
        var monitor = new MemberActivityMonitor();

        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "busy", null, "test")]);
        Assert.Single(monitor.GetLeaders(@"C:\Work"));

        monitor.RefreshBusySessions([]);
        Assert.Empty(monitor.GetLeaders(@"C:\Work"));
    }

    [Fact]
    public void PollTrackedSessionsで直近に触ったファイルパスがGetLeadersのCurrentWorkPathに反映される()
    {
        var tempDir = CreateTempProjectsDir(out var restoreHome);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Work";
            var transcriptPath = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
            File.WriteAllText(transcriptPath, EditLine(@"C:\Work\AgentDeskApp\a.cs") + "\n");

            var monitor = new MemberActivityMonitor();
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "interactive", "busy", null, "test")]);
            monitor.PollTrackedSessions();

            Assert.Equal(@"C:\Work\AgentDeskApp\a.cs", monitor.GetLeaders(cwd)[0].CurrentWorkPath);
        }
        finally
        {
            restoreHome();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GeminiセッションのサブエージェントがRunningおよびDoneに反映される()
    {
        var monitor = new MemberActivityMonitor();

        // 1. Geminiセッションで "code-reviewer" と "qa-engineer" が Running 中
        monitor.GeminiScanner = _ =>
        [
            new GeminiSessionRecord(
                ConversationId: "conv1",
                Cwd: @"C:\Studio",
                State: MemberActivityState.Running,
                LastWorkPath: @"C:\Studio\test.cs",
                LastModified: DateTime.UtcNow,
                ActiveSubagents: ["code-reviewer", "qa-engineer"])
        ];

        monitor.PollTrackedSessions();

        Assert.Equal(MemberActivityState.Running, monitor.GetState("code-reviewer"));
        Assert.Equal(MemberActivityState.Running, monitor.GetState("qa-engineer"));
        Assert.Equal(MemberActivityState.Idle, monitor.GetState("tech-researcher"));

        // 2. Geminiセッションが完了 (Done)
        monitor.GeminiScanner = _ =>
        [
            new GeminiSessionRecord(
                ConversationId: "conv1",
                Cwd: @"C:\Studio",
                State: MemberActivityState.Done,
                LastWorkPath: @"C:\Studio\test.cs",
                LastModified: DateTime.UtcNow,
                ActiveSubagents: ["code-reviewer", "qa-engineer"])
        ];

        monitor.PollTrackedSessions();

        Assert.Equal(MemberActivityState.Done, monitor.GetState("code-reviewer"));
        Assert.Equal(MemberActivityState.Done, monitor.GetState("qa-engineer"));
    }

    [Fact]
    public void backgroundセッションもU25で追跡されRunningに遷移する()
    {
        var tempDir = CreateTempProjectsDir(out var restoreHome);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Project";
            var transcriptPath = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
            File.WriteAllText(transcriptPath, ToolUseLine("toolu_1", "researcher") + "\n");

            var monitor = new MemberActivityMonitor();
            // 外部CLIやメインチャットから "claude --bg" 等で起動されたセッション(kind=background)。
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "background", null, "running", "test")]);
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Running, monitor.GetState("researcher"));
        }
        finally
        {
            restoreHome();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void backgroundセッションがstopped状態になると追跡をやめる()
    {
        var tempDir = CreateTempProjectsDir(out var restoreHome);
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var cwd = @"C:\Fake\Project";
            var transcriptPath = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
            File.WriteAllText(transcriptPath, ToolUseLine("toolu_1", "researcher") + "\n");

            var monitor = new MemberActivityMonitor();
            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "background", null, "running", "test")]);
            monitor.PollTrackedSessions();
            Assert.Equal(MemberActivityState.Running, monitor.GetState("researcher"));

            monitor.RefreshBusySessions([new AgentSessionInfo(sessionId, cwd, "background", null, "stopped", "test")]);
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Done, monitor.GetState("researcher"));
        }
        finally
        {
            restoreHome();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ReportDirectLaunchOutcomeでCancelledとTimedOutが即時反映される()
    {
        var monitor = new MemberActivityMonitor();

        monitor.ReportDirectLaunchOutcome("researcher", MemberActivityState.Cancelled);
        Assert.Equal(MemberActivityState.Cancelled, monitor.GetState("researcher"));

        monitor.ReportDirectLaunchOutcome("coder", MemberActivityState.TimedOut);
        Assert.Equal(MemberActivityState.TimedOut, monitor.GetState("coder"));
    }

    [Fact]
    public void ReportDirectLaunchOutcomeは直接起動プロセスの追跡を止めポーリングで上書きされない()
    {
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c timeout /t 30",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            var monitor = new MemberActivityMonitor();
            monitor.RegisterDirectLaunch("researcher", proc);
            Assert.Equal(MemberActivityState.Running, monitor.GetState("researcher"));

            monitor.ReportDirectLaunchOutcome("researcher", MemberActivityState.Cancelled);
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Cancelled, monitor.GetState("researcher"));
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
        }
    }

    private static string EditLine(string filePath) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Edit\",\"input\":{\"file_path\":\"" +
        filePath.Replace("\\", "\\\\") + "\"}}]}}";

    private static string ToolUseLine(string toolUseId, string subagentName) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + toolUseId +
        "\",\"name\":\"Agent\",\"input\":{\"subagent_type\":\"" + subagentName + "\"}}]}}";

    /// <summary>
    /// テスト用に%USERPROFILE%を一時フォルダへ差し替える。
    /// ClaudeTranscriptPathResolverが%USERPROFILE%\.claude\projects\配下を参照するため。
    /// </summary>
    private static string CreateTempProjectsDir(out Action restore)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "member-activity-monitor-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var original = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", tempDir);
        restore = () => Environment.SetEnvironmentVariable("USERPROFILE", original);

        return tempDir;
    }
}
