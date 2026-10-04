using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="MemberActivityMonitor"/>のAgent/Task命令文中のメンバー名による「作業中」の状態遷移テスト(BUG-8)。
/// 汎用エージェントへの委任でも命令文のメンバー名がRunningになり、tool_resultでDoneになることを確認する。
/// </summary>
[Collection("UserProfileEnv")]
public class MemberActivityMonitorNominationTests
{
    private static readonly PromptNominationCandidate[] NominationCandidates =
        [new("tsukuru", "ツクル"), new("shirabe", "シラベ")];

    private static string ToolUseLine(string toolUseId, string subagentType, string prompt) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + toolUseId +
        "\",\"name\":\"Agent\",\"input\":{\"subagent_type\":\"" + subagentType + "\",\"prompt\":\"" + prompt + "\"}}]}}";

    private static string ToolResultLine(string toolUseId) =>
        "{\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + toolUseId + "\",\"content\":\"ok\"}]}}";

    private static AgentSessionInfo BackgroundSession(string sessionId, string cwd, string state) =>
        new(sessionId, cwd, "background", null, state, "t");

    /// <summary>%USERPROFILE%を一時フォルダへ差し替える(会話ログの解決先を隔離するため)。</summary>
    private static string CreateTempProjectsDir(out Action restore)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "member-nomination-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var original = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", tempDir);
        restore = () => Environment.SetEnvironmentVariable("USERPROFILE", original);
        return tempDir;
    }

    /// <summary>テスト用の監視インスタンス・セッションID・cwd・会話ログのパスを用意する。</summary>
    private static (MemberActivityMonitor Monitor, string SessionId, string Cwd, string Path) Setup()
    {
        var sessionId = Guid.NewGuid().ToString();
        var cwd = @"C:\Fake\Project";
        var path = ClaudeTranscriptPathResolver.Resolve(cwd, sessionId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var monitor = new MemberActivityMonitor { NominationCandidatesProvider = () => NominationCandidates };
        return (monitor, sessionId, cwd, path);
    }

    /// <summary>一時フォルダ差し替え付きでテスト本体を実行し、後始末する。</summary>
    private static void WithTempHome(Action<MemberActivityMonitor, string, string, string> body)
    {
        var tempDir = CreateTempProjectsDir(out var restore);
        try
        {
            var (monitor, sessionId, cwd, path) = Setup();
            body(monitor, sessionId, cwd, path);
        }
        finally
        {
            restore();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void 汎用エージェントへの命令文のメンバー名で作業中になりtool_resultでDoneになる()
    {
        WithTempHome((monitor, sessionId, cwd, path) =>
        {
            File.WriteAllText(path, ToolUseLine("toolu_1", "general-purpose", "あなたは開発チームのソフトウェアエンジニア『ツクル』です。実装してください") + "\n");
            monitor.RefreshBusySessions([BackgroundSession(sessionId, cwd, "running")]);
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Running, monitor.GetState("tsukuru"));
            Assert.Equal(MemberActivityState.Idle, monitor.GetState("shirabe"));

            File.AppendAllText(path, ToolResultLine("toolu_1") + "\n");
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Done, monitor.GetState("tsukuru"));
        });
    }

    [Fact]
    public void 命令文にメンバー名が無ければメンバーは作業中にならない()
    {
        WithTempHome((monitor, sessionId, cwd, path) =>
        {
            File.WriteAllText(path, ToolUseLine("toolu_1", "general-purpose", "コードを調べてください") + "\n");
            monitor.RefreshBusySessions([BackgroundSession(sessionId, cwd, "running")]);
            monitor.PollTrackedSessions();

            Assert.Equal(MemberActivityState.Idle, monitor.GetState("tsukuru"));
        });
    }
}
