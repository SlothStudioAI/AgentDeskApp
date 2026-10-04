using System.Text;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary><see cref="DiagnosticLog"/>(診断ログのファイル出力)のテスト。</summary>
public class DiagnosticLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "difflog-" + Guid.NewGuid());

    private string LogPath => Path.Combine(_dir, "logs", "agentdesk-diagnostic.log");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void 行頭にローカル時刻がミリ秒まで付き追記される()
    {
        var log = new DiagnosticLog(LogPath, true, 1_000_000) { Clock = () => new DateTime(2026, 10, 4, 2, 21, 5, 123) };

        log.Write("一行目");
        log.Write("二行目");

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(2, lines.Length);
        Assert.Equal("2026-10-04 02:21:05.123 一行目", lines[0]);
        Assert.EndsWith("二行目", lines[1]);
    }

    [Fact]
    public void 無効のときは何も書かずフォルダも作らない()
    {
        var log = new DiagnosticLog(LogPath, false, 1_000_000);

        log.Write("書かれない");

        Assert.False(File.Exists(LogPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(LogPath)));
    }

    [Fact]
    public void 上限に達したら1へ退避し最大2ファイルになる()
    {
        // 1行は約40バイト。上限100バイトなら数行ごとに退避する
        var log = new DiagnosticLog(LogPath, true, 100);

        for (var i = 0; i < 30; i++)
        {
            log.Write($"line{i:D2}");
        }

        var files = Directory.GetFiles(Path.GetDirectoryName(LogPath)!).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["agentdesk-diagnostic.log", "agentdesk-diagnostic.log.1"], files);
        Assert.True(new FileInfo(LogPath).Length <= 100);
        Assert.True(new FileInfo(LogPath + ".1").Length <= 100);
        // 最新の行は現行ファイルにあり、古い行(line00)は2世代を超えて消えている
        Assert.Contains("line29", File.ReadAllText(LogPath));
        Assert.DoesNotContain("line00", File.ReadAllText(LogPath + ".1"));
    }

    [Fact]
    public void 書き込みに失敗しても例外を出さず次回は回復する()
    {
        // 出力先の「ファイル」の場所にフォルダがあって書けない状況を作る
        Directory.CreateDirectory(LogPath);
        var log = new DiagnosticLog(LogPath, true, 1_000_000);

        var ex = Record.Exception(() => log.Write("失敗する"));
        Assert.Null(ex);

        Directory.Delete(LogPath);
        log.Write("成功する");
        Assert.Contains("成功する", File.ReadAllText(LogPath));
    }

    [Fact]
    public void 改行を含むメッセージも1行にまとめる()
    {
        var log = new DiagnosticLog(LogPath, true, 1_000_000);

        log.Write("a\r\nb\nc");

        Assert.Single(File.ReadAllLines(LogPath));
    }

    [Fact]
    public void WriteOnChangeは内容が変わったときだけ書く()
    {
        var log = new DiagnosticLog(LogPath, true, 1_000_000);

        log.WriteOnChange("k", "同じ");
        log.WriteOnChange("k", "同じ");
        log.WriteOnChange("k", "変わった");
        log.WriteOnChange("k", "同じ");

        Assert.Equal(3, File.ReadAllLines(LogPath).Length);
    }

    [Fact]
    public void 既存ファイルのサイズを引き継いで上限判定する()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllText(LogPath, new string('x', 95), Encoding.UTF8);
        var log = new DiagnosticLog(LogPath, true, 100);

        log.Write("新しい行");

        Assert.True(File.Exists(LogPath + ".1"));
        Assert.Contains("新しい行", File.ReadAllText(LogPath));
    }

    [Fact]
    public void 既定の出力先はAgentDeskAppフォルダ配下のlogsになる()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentDeskApp", "logs", "agentdesk-diagnostic.log");
        Assert.Equal(expected, DiagnosticLog.DefaultPath);
    }

    [Fact]
    public void 設定の既定値は診断ログ無効で上限1MBと最短表示3秒()
    {
        var s = AppSettings.Default;
        Assert.False(s.DiagnosticLogEnabled);
        Assert.Equal(1048576, s.EffectiveDiagnosticLogMaxBytes);
        Assert.Equal(3, s.EffectiveMinRunningDisplaySeconds);
        Assert.Equal(1048576, (s with { DiagnosticLogMaxBytes = 0 }).EffectiveDiagnosticLogMaxBytes);
        Assert.Equal(0, (s with { MinRunningDisplaySeconds = 0 }).EffectiveMinRunningDisplaySeconds);
    }

    [Fact]
    public void サポート画面は診断ログの場所を指定時だけ表示する()
    {
        var with = SupportContent.Build(new Version(0, 9, 0), @"C:\x\settings.json", @"C:\x\logs\d.log");
        var without = SupportContent.Build(new Version(0, 9, 0), @"C:\x\settings.json");

        Assert.Equal(6, with.Count);
        Assert.Contains(with, s => s.CopyableText == @"C:\x\logs\d.log");
        Assert.Equal(5, without.Count);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"DiagnosticLogEnabled\": true}", true)]
    [InlineData("{\"DiagnosticLogEnabled\": false}", false)]
    public void settingsのDiagnosticLogEnabledは未記載ならオフで明示値は尊重される(string json, bool expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "adtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, json);
            Assert.Equal(expected, AppSettingsLoader.Load(path).DiagnosticLogEnabled);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void サポート画面の診断ログ説明は既定オフと有効化方法を案内する()
    {
        var with = SupportContent.Build(new Version(0, 9, 0), @"C:\x\settings.json", @"C:\x\logs\d.log");
        var section = with.Single(s => s.CopyableText == @"C:\x\logs\d.log");

        Assert.Contains("既定ではオフ", section.Body);
        Assert.Contains("\"DiagnosticLogEnabled\": true", section.Body);
        Assert.Contains("false", section.Body);
        Assert.Contains("会話内容は含みません", section.Body);
    }
}
