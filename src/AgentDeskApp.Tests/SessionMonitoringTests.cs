using System.IO;
using System.Threading;
using AgentDeskApp;
using Xunit;

namespace AgentDeskApp.Tests;

public class SessionMonitoringTests
{
    [Fact]
    public void AgentTaskHistoryWindow_初期化時に例外が発生しない()
    {
        var agent = new AgentDefinition(
            Name: "designer",
            Description: "なまけものデザイナー",
            Tools: "Read, Edit",
            Model: "sonnet",
            Color: "#10B981",
            Body: "デザイン専門",
            Scope: AgentScope.Global,
            FilePath: Path.Combine(Path.GetTempPath(), "designer.md"),
            DisplayName: "なまけものデザイナー",
            AvatarPath: null,
            Engine: AgentEngineKind.Shared,
            GeminiSkillPath: null);

        Exception? thrown = null;

        var thread = new Thread(() =>
        {
            try
            {
                var win = new AgentTaskHistoryWindow(agent);
                Assert.NotNull(win);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(thrown);
    }

    [Fact]
    public void GeminiSessionScanner_ScanActiveSessionsが例外なく実行できる()
    {
        var sessions = GeminiSessionScanner.ScanActiveSessions();
        Assert.NotNull(sessions);
    }

    [Fact]
    public void GeminiSessionScanner_現在のワークスペースでリーダーが検出できる()
    {
        var rawSessions = GeminiSessionScanner.ScanActiveSessions(300);
        foreach (var s in rawSessions)
        {
            System.Diagnostics.Debug.WriteLine($"Found session: {s.ConversationId}, Cwd: {s.Cwd}, State: {s.State}, LastWork: {s.LastWorkPath}");
        }

        Assert.NotEmpty(rawSessions);
        var targetCwd = rawSessions.First(s => s.Cwd != null).Cwd!;

        var monitor = new MemberActivityMonitor
        {
            // 実機のログを使うため、直近の範囲は上で走査した300分に合わせる(既定の5分だと環境次第で0件になる)
            GeminiRecentMinutes = 300,
            GeminiScanner = GeminiSessionScanner.ScanActiveSessions
        };
        var leaders = monitor.GetLeaders(targetCwd);
        var geminiLeader = leaders.FirstOrDefault(l => l.Engine == AgentEngineKind.Gemini);

        Assert.NotNull(geminiLeader);
        Assert.Equal(AgentEngineKind.Gemini, geminiLeader.Engine);
        Assert.False(string.IsNullOrEmpty(geminiLeader.SessionId));
    }


    [Fact]
    public void MemberActivityMonitor_直接起動したプロセスがActiveになり終了でDoneになる()
    {
        var monitor = new MemberActivityMonitor();
        const string agentName = "test-designer";

        // 初期状態はIdle
        Assert.Equal(MemberActivityState.Idle, monitor.GetState(agentName));

        // ダミープロセスを起動(cmd.exe /c ping 127.0.0.1 -n 2 など短時間動くもの)
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 2 > nul",
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        Assert.NotNull(proc);

        // 登録直後はRunning
        monitor.RegisterDirectLaunch(agentName, proc);
        Assert.Equal(MemberActivityState.Running, monitor.GetState(agentName));

        // プロセスの終了を待つ
        proc.WaitForExit(5000);

        // 終了後はDone
        Assert.Equal(MemberActivityState.Done, monitor.GetState(agentName));
    }
}
