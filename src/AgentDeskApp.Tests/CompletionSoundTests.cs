using System.IO;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="CompletionSound"/>(完了音ファイルの解決)に関するテスト。実際の再生は行わない。
/// </summary>
public class CompletionSoundTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSoundFile_未指定なら標準音を使うためnullを返す(string? configuredPath)
    {
        Assert.Null(CompletionSound.ResolveSoundFile(configuredPath));
    }

    [Fact]
    public void ResolveSoundFile_存在しないファイルならnullを返す()
    {
        var path = Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid() + ".wav");

        Assert.Null(CompletionSound.ResolveSoundFile(path));
    }

    [Fact]
    public void ResolveSoundFile_不正な文字を含むパスでも例外にならずnullを返す()
    {
        Assert.Null(CompletionSound.ResolveSoundFile("C:\\bad\0path.wav"));
    }

    [Fact]
    public void ResolveSoundFile_存在するファイルならフルパスを返す_引用符付きでも可()
    {
        var path = Path.Combine(Path.GetTempPath(), "sound-test-" + Guid.NewGuid() + ".wav");
        File.WriteAllBytes(path, [0]);
        try
        {
            Assert.Equal(Path.GetFullPath(path), CompletionSound.ResolveSoundFile(path));
            Assert.Equal(Path.GetFullPath(path), CompletionSound.ResolveSoundFile($"\"{path}\""));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
