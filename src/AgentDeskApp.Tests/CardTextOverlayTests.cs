using System.IO;
using System.Windows.Media;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// カード下部の明るい下地(<see cref="CardTextOverlay"/>)と、その色・文字色の設定値(<see cref="AppSettings"/>)に関するテスト(2026-10-03)。
/// </summary>
public class CardTextOverlayTests
{
    /// <summary>既定値が「白い下地(不透明度0.9)＋濃いグレーの文字」になっていること。</summary>
    [Fact]
    public void Default_下地は白で文字は濃いグレー()
    {
        var settings = AppSettings.Default;

        Assert.Equal("#FFFFFF", settings.EffectiveCardBottomOverlayColor);
        Assert.Equal(0.9, settings.EffectiveCardBottomOverlayOpacity);
        Assert.Equal("#374151", settings.EffectiveCardTextColor);
        Assert.Equal("#111827", settings.EffectiveCardNameTextColor);
    }

    /// <summary>設定ファイルに書いた下地の色・濃さ・文字色が読み込まれること。</summary>
    [Fact]
    public void Load_下地と文字色の設定を読み込める()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-test-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, """
            {
              "CardBottomOverlayColor": "#F8FAFC",
              "CardBottomOverlayOpacity": 0.75,
              "CardTextColor": "#1F2937",
              "CardNameTextColor": "#000000"
            }
            """);
        try
        {
            var settings = AppSettingsLoader.Load(path);

            Assert.Equal("#F8FAFC", settings.EffectiveCardBottomOverlayColor);
            Assert.Equal(0.75, settings.EffectiveCardBottomOverlayOpacity);
            Assert.Equal("#1F2937", settings.EffectiveCardTextColor);
            Assert.Equal("#000000", settings.EffectiveCardNameTextColor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>色の形式でない値・範囲外の濃さは既定値に置き換わること(0は「下地なし」として有効)。</summary>
    [Fact]
    public void Effective値_不正な色や範囲外の濃さは既定値に置き換わる()
    {
        var settings = AppSettings.Default with
        {
            CardBottomOverlayColor = "white",
            CardBottomOverlayOpacity = 1.5,
            CardTextColor = "",
            CardNameTextColor = null,
        };

        Assert.Equal(AppSettings.DefaultCardBottomOverlayColor, settings.EffectiveCardBottomOverlayColor);
        Assert.Equal(AppSettings.DefaultCardBottomOverlayOpacity, settings.EffectiveCardBottomOverlayOpacity);
        Assert.Equal(AppSettings.DefaultCardTextColor, settings.EffectiveCardTextColor);
        Assert.Equal(AppSettings.DefaultCardNameTextColor, settings.EffectiveCardNameTextColor);

        Assert.Equal(AppSettings.DefaultCardBottomOverlayOpacity, (AppSettings.Default with { CardBottomOverlayOpacity = -0.1 }).EffectiveCardBottomOverlayOpacity);
        Assert.Equal(0, (AppSettings.Default with { CardBottomOverlayOpacity = 0 }).EffectiveCardBottomOverlayOpacity);
    }

    /// <summary>
    /// 下地は上端が完全に透明・下端が設定した濃さで、下に行くほど濃くなる(途中で薄くならない)こと。
    /// 文字が載る範囲(位置0.6より下)では、いちばん下の約7割以上の濃さがあること。
    /// </summary>
    [Fact]
    public void ComputeStops_上は透明で下ほど濃く文字の範囲は十分に濃い()
    {
        var stops = CardTextOverlay.ComputeStops(0.9);

        Assert.Equal(0.0, stops[0].Offset);
        Assert.Equal(0.0, stops[0].Opacity);
        Assert.Equal(1.0, stops[^1].Offset);
        Assert.Equal(0.9, stops[^1].Opacity, 6);

        for (var i = 1; i < stops.Count; i++)
        {
            Assert.True(stops[i].Offset >= stops[i - 1].Offset);
            Assert.True(stops[i].Opacity >= stops[i - 1].Opacity);
        }

        Assert.All(stops.Where(s => s.Offset >= 0.62), s => Assert.True(s.Opacity >= 0.9 * 0.7));
    }

    /// <summary>濃さは0〜1に丸められること。</summary>
    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    public void ComputeStops_濃さは0から1に丸められる(double configured, double expectedBottom)
    {
        var stops = CardTextOverlay.ComputeStops(configured);

        Assert.Equal(expectedBottom, stops[^1].Opacity, 6);
        Assert.All(stops, s => Assert.InRange(s.Opacity, 0.0, 1.0));
    }

    /// <summary>作ったブラシの各点が、指定した色と、計算した濃さ(0〜255)になっていること。</summary>
    [Fact]
    public void CreateBottomOverlayBrush_指定した色と濃さのグラデーションになる()
    {
        var brush = CardTextOverlay.CreateBottomOverlayBrush(Colors.White, 0.9);
        var stops = CardTextOverlay.ComputeStops(0.9);

        Assert.True(brush.IsFrozen);
        Assert.Equal(stops.Count, brush.GradientStops.Count);
        Assert.Equal(0, brush.GradientStops[0].Color.A);
        Assert.Equal((byte)Math.Round(0.9 * 255), brush.GradientStops[^1].Color.A);
        Assert.All(brush.GradientStops, s => Assert.Equal((255, 255, 255), (s.Color.R, s.Color.G, s.Color.B)));
    }

    /// <summary>色の文字列が変換できなければ予備の色になること。</summary>
    [Fact]
    public void ToColor_変換できなければ予備の色になる()
    {
        Assert.Equal(Color.FromRgb(0x37, 0x41, 0x51), CardTextOverlay.ToColor("#374151", "#000000"));
        Assert.Equal(Colors.White, CardTextOverlay.ToColor("#ZZZZZZ", "#FFFFFF"));
    }
}
