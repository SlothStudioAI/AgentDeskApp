using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="CliPathResolver"/>の候補パス解決ロジックのテスト(U-26)。存在確認はデリゲートで差し替え、実ファイルに依存しない。
/// </summary>
public class CliPathResolverTests
{
    [Fact]
    public void PATH上で解決できるなら既定名を返す()
    {
        var result = CliPathResolver.Resolve("agy", new[] { @"C:\fallback\agy.exe" },
            p => p == @"C:\tools\agy.exe", @"C:\x;C:\tools", ".EXE;.CMD");
        Assert.Equal("agy", result);
    }

    [Fact]
    public void PATHで解決できなければ実在する最初の候補のフルパスを返す()
    {
        var result = CliPathResolver.Resolve("agy", new[] { @"C:\a\agy.exe", @"C:\b\agy.exe", @"C:\c\agy.exe" },
            p => p == @"C:\b\agy.exe" || p == @"C:\c\agy.exe", @"C:\x", ".EXE");
        Assert.Equal(@"C:\b\agy.exe", result);
    }

    [Fact]
    public void どこにも無ければ既定名を返す()
    {
        var result = CliPathResolver.Resolve("claude", new[] { @"C:\a\claude.cmd" }, _ => false, @"C:\x", ".EXE");
        Assert.Equal("claude", result);
    }

    [Fact]
    public void ユーザー明示設定のパスは探索せず最優先で使われる()
    {
        var settings = AppSettings.Default with { ClaudeCliPath = @"D:\my\claude.exe" };
        Assert.Equal(@"D:\my\claude.exe", settings.EffectiveClaudeCliPath);
    }
}
