using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="SessionWorkspaceTracker"/>(会話ログから直近に触ったファイルパスを追跡)に関するテスト。
/// </summary>
public class SessionWorkspaceTrackerTests
{
    private static string EditLine(string filePath) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Edit\",\"input\":{\"file_path\":\"" +
        filePath.Replace("\\", "\\\\") + "\"}}]}}";

    private static string GlobLine(string path) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Glob\",\"input\":{\"path\":\"" +
        path.Replace("\\", "\\\\") + "\"}}]}}";

    [Fact]
    public void Poll_ファイルが無ければCurrentPathはnull()
    {
        var tracker = new SessionWorkspaceTracker();
        tracker.Poll(Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid() + ".jsonl"));

        Assert.Null(tracker.CurrentPath);
    }

    [Fact]
    public void Poll_Editのfile_pathを拾う()
    {
        var path = CreateTempFile(EditLine(@"C:\Work\AgentDeskApp\src\AgentDeskApp\MainWindow.xaml.cs") + "\n");
        try
        {
            var tracker = new SessionWorkspaceTracker();
            tracker.Poll(path);

            Assert.Equal(@"C:\Work\AgentDeskApp\src\AgentDeskApp\MainWindow.xaml.cs", tracker.CurrentPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_Globのpathを拾う()
    {
        var path = CreateTempFile(GlobLine(@"C:\Work\sample-project") + "\n");
        try
        {
            var tracker = new SessionWorkspaceTracker();
            tracker.Poll(path);

            Assert.Equal(@"C:\Work\sample-project", tracker.CurrentPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_複数回の追記で最新のパスに更新される()
    {
        var path = CreateTempFile(EditLine(@"C:\Work\AgentDeskApp\a.cs") + "\n");
        try
        {
            var tracker = new SessionWorkspaceTracker();
            tracker.Poll(path);
            Assert.Equal(@"C:\Work\AgentDeskApp\a.cs", tracker.CurrentPath);

            File.AppendAllText(path, EditLine(@"C:\Work\sample-project\b.md") + "\n");
            tracker.Poll(path);

            Assert.Equal(@"C:\Work\sample-project\b.md", tracker.CurrentPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Poll_対象外のツールは無視する()
    {
        var line = """
            {"message":{"role":"assistant","content":[{"type":"tool_use","id":"t1","name":"WebSearch","input":{"query":"x"}}]}}
            """.ReplaceLineEndings("");
        var path = CreateTempFile(line + "\n");
        try
        {
            var tracker = new SessionWorkspaceTracker();
            tracker.Poll(path);

            Assert.Null(tracker.CurrentPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "session-workspace-tracker-test-" + Guid.NewGuid() + ".jsonl");
        File.WriteAllText(path, content);
        return path;
    }
}
