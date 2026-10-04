using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="CliAvailability"/>のCLI検出に関するテスト。
/// スペースや&amp;・()を含むパスでもcmd.exe経由の呼び出しが壊れないこと(BUG-5)を確認する。
/// </summary>
public class CliAvailabilityTests
{
    [Fact]
    public async Task 特殊文字を含むフォルダの_cmdシムでもバージョンを検出できる()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "cli-avail test & co (x) " + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(baseDir);
        try
        {
            var shim = Path.Combine(baseDir, "fake-cli.cmd");
            File.WriteAllText(shim, "@echo off\r\necho fake-cli 1.2.3\r\n");

            var version = await CliAvailability.DetectVersionAsync(shim);

            Assert.Equal("fake-cli 1.2.3", version);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void 実在するexeはcmdを介さず直接起動する()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "cli-avail exe & 100% " + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(baseDir);
        try
        {
            var exe = Path.Combine(baseDir, "tool.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), exe);

            var psi = CliAvailability.CreateVersionStartInfo(exe);

            Assert.Equal(exe, psi.FileName);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void 引用符付きで渡されたパスも扱える()
    {
        var psi = CliAvailability.CreateVersionStartInfo("\"C:\\Program Files\\x y\\claude.cmd\"");

        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Contains("\"C:\\Program Files\\x y\\claude.cmd\"", psi.Arguments);
        Assert.DoesNotContain("\"\"\"", psi.Arguments);
    }

    [Fact]
    public async Task 存在しないコマンドはnullを返す()
    {
        var version = await CliAvailability.DetectVersionAsync("definitely-not-a-real-command-" + Guid.NewGuid().ToString("N"));

        Assert.Null(version);
    }
}
