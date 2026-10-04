using System.Windows.Media;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// 頭文字アイコン(アバター画像なしの表示)の背景・頭文字の決め方が、色設定に依存しないことを確認するテスト。
/// </summary>
public class AvatarInitialStyleTests
{
    /// <summary>背景は引数なしで決まり、固定の灰青グラデーションになること(色設定を受け取る口が無い)。</summary>
    [Fact]
    public void CreateBackground_色設定に関係なく灰青グラデーション()
    {
        var a = AvatarInitialStyle.CreateBackground();
        var b = AvatarInitialStyle.CreateBackground();

        Assert.Equal(2, a.GradientStops.Count);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#94A3B8")!, a.GradientStops[0].Color);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#475569")!, a.GradientStops[1].Color);
        Assert.Equal(a.GradientStops[0].Color, b.GradientStops[0].Color);
        Assert.True(a.IsFrozen);
    }

    /// <summary>頭文字は先頭1文字(大文字化しない)で、空なら「?」になること。</summary>
    [Theory]
    [InlineData("要件定義", "要")]
    [InlineData("  tsutae", "t")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void GetInitial_先頭1文字(string? name, string expected)
    {
        Assert.Equal(expected, AvatarInitialStyle.GetInitial(name));
    }
}
