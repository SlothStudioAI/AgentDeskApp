using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="AgentsJsonClient"/>(claude agents --jsonの解析)に関するテスト。
/// 実機で確認した実際の出力形式を元にしたサンプルを使う。
/// </summary>
public class AgentsJsonClientTests
{
    private const string SampleJson = """
        [
          {
            "id": "4ae96c8f",
            "cwd": "C:\\Work\\SampleProject",
            "kind": "background",
            "startedAt": 1790244223749,
            "sessionId": "4ae96c8f-c096-4e34-bfad-c713e256f0b0",
            "name": "(仮)なまえ未定1",
            "state": "blocked"
          },
          {
            "pid": 13004,
            "cwd": "C:\\Work",
            "kind": "interactive",
            "startedAt": 1790302742108,
            "sessionId": "c91ffde0-eae9-4707-b139-371fd40ccc2c",
            "name": "20260925_AIシェアオフィス続き",
            "status": "idle"
          },
          {
            "pid": 34996,
            "cwd": "c:\\Work",
            "kind": "interactive",
            "startedAt": 1790322097931,
            "sessionId": "252c4b78-83ad-4008-9b16-88caa4f97b32",
            "name": "sample-session",
            "status": "busy"
          }
        ]
        """;

    [Fact]
    public void Parse_全件読み込める()
    {
        var sessions = AgentsJsonClient.Parse(SampleJson);

        Assert.Equal(3, sessions.Count);
    }

    [Fact]
    public void Parse_interactiveでstatusBusyのみIsBusyがtrue()
    {
        var sessions = AgentsJsonClient.Parse(SampleJson);

        Assert.False(sessions[0].IsBusy); // background/state=blocked
        Assert.False(sessions[1].IsBusy); // interactive/status=idle
        Assert.True(sessions[2].IsBusy);  // interactive/status=busy
    }

    [Fact]
    public void Parse_フィールドを正しく読み取れる()
    {
        var sessions = AgentsJsonClient.Parse(SampleJson);
        var busy = sessions[2];

        Assert.Equal("252c4b78-83ad-4008-9b16-88caa4f97b32", busy.SessionId);
        Assert.Equal("c:\\Work", busy.Cwd);
        Assert.Equal("interactive", busy.Kind);
        Assert.Equal("busy", busy.Status);
        Assert.Null(busy.State);
    }

    [Fact]
    public void Parse_空文字は空一覧を返す()
    {
        Assert.Empty(AgentsJsonClient.Parse(""));
        Assert.Empty(AgentsJsonClient.Parse("   "));
    }

    [Fact]
    public void Parse_空配列は空一覧を返す()
    {
        Assert.Empty(AgentsJsonClient.Parse("[]"));
    }
}
