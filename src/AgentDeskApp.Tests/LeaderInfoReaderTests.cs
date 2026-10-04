using System.IO;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="LeaderInfoReader"/>(CLAUDE.mdの「## リーダー」セクション読み取り)に関するテスト。
/// </summary>
public class LeaderInfoReaderTests
{
    [Fact]
    public void Read_ファイルが無ければ両方null()
    {
        var path = Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid() + ".md");

        var info = LeaderInfoReader.Read(path);

        Assert.Null(info.Name);
        Assert.Null(info.Role);
    }

    [Fact]
    public void Read_セクションが無ければ両方null()
    {
        var path = CreateTempFile("# 何かのルール\n本文だけ。\n");

        try
        {
            var info = LeaderInfoReader.Read(path);
            Assert.Null(info.Name);
            Assert.Null(info.Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_名前と役割を読み取れる()
    {
        var path = CreateTempFile("""
            # ルール

            ## リーダー
            - 名前: ゆやぴよ
            - 役割: 指示者

            ## 別のセクション
            関係ない本文。
            """);

        try
        {
            var info = LeaderInfoReader.Read(path);
            Assert.Equal("ゆやぴよ", info.Name);
            Assert.Equal("指示者", info.Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_全角コロンでも読み取れる()
    {
        var path = CreateTempFile("## リーダー\n- 名前：ゆやぴよ\n");

        try
        {
            var info = LeaderInfoReader.Read(path);
            Assert.Equal("ゆやぴよ", info.Name);
            Assert.Null(info.Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_名前だけあれば役割はnull()
    {
        var path = CreateTempFile("## リーダー\n- 名前: ゆやぴよ\n");

        try
        {
            var info = LeaderInfoReader.Read(path);
            Assert.Equal("ゆやぴよ", info.Name);
            Assert.Null(info.Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "leader-info-reader-test-" + Guid.NewGuid() + ".md");
        File.WriteAllText(path, content);
        return path;
    }
}
