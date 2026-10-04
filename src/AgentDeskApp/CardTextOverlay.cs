using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AgentDeskApp;

/// <summary>
/// メンバー・リーダーカードの下部に文字を載せるための「明るい下地」と文字装飾をまとめたもの(2026-10-03)。
/// 以前はカード下部を暗いグラデーションで覆って白文字を載せていたが、ユーザー要望(「暗くせず、文字色を濃いグレーで」)により、
/// 下ほど白く・上に向かって透明になる半透明のグラデーションを敷き、その上に濃いグレーの文字を載せる方式に変えた。
/// 揺れ(傾き)でカード下の角に出る数pxの隙間も、この下地(いちばん下が最も濃い)で目立たなくする。
/// </summary>
public static class CardTextOverlay
{
    /// <summary>
    /// 下地のグラデーションの形(位置, いちばん下の不透明度に対する割合)。位置は0=カード上端、1=カード下端。
    /// カード上部(絵の顔が来るあたり)は完全に透明にし、文字が載る下部約45%で一気に白くする。
    /// 文字の載る範囲(名前の行は位置0.6前後から)では、いちばん下の約7割以上の濃さになる。
    /// 見た目の形を決める固定値(濃さと色は設定値で変えられる)。
    /// </summary>
    private static readonly (double Offset, double Ratio)[] OverlayShape =
    [
        (0.0, 0.0),
        (0.35, 0.0),
        (0.50, 0.55),
        (0.62, 0.85),
        (0.75, 0.97),
        (1.0, 1.0),
    ];

    /// <summary>
    /// 下地のグラデーションの各点(位置と不透明度)を計算する。
    /// </summary>
    /// <param name="maxOpacity">いちばん下(最も濃いところ)の不透明度(0〜1。範囲外は0〜1に丸める)。</param>
    /// <returns>上から順の (位置, 不透明度) の一覧。</returns>
    public static IReadOnlyList<(double Offset, double Opacity)> ComputeStops(double maxOpacity)
    {
        var clamped = Math.Clamp(maxOpacity, 0, 1);
        return OverlayShape.Select(s => (s.Offset, s.Ratio * clamped)).ToList();
    }

    /// <summary>
    /// カード下部に敷く、下ほど濃く上に向かって透明になる半透明グラデーションを作る(Freeze済み)。
    /// </summary>
    /// <param name="color">下地の色(既定は白)。色の不透明度は無視し、<paramref name="maxOpacity"/> で濃さを決める。</param>
    /// <param name="maxOpacity">いちばん下(最も濃いところ)の不透明度(0〜1)。</param>
    /// <returns>カード全面に重ねて使うグラデーションブラシ。</returns>
    public static LinearGradientBrush CreateBottomOverlayBrush(Color color, double maxOpacity)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (offset, opacity) in ComputeStops(maxOpacity))
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B), offset));
        }

        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 濃い文字の周りにうっすら白いふちを付ける効果(Freeze済み)。下地が薄い部分で絵の暗い色と文字が重なっても読めるようにする。
    /// 以前の白文字用の黒い影の代わり。見た目の調整用の固定値で、複数の要素で共有してよい。
    /// </summary>
    /// <param name="haloColor">ふちの色(下地と同じ色を渡す)。</param>
    /// <returns>文字に付ける効果。</returns>
    public static DropShadowEffect CreateTextHalo(Color haloColor)
    {
        var effect = new DropShadowEffect
        {
            BlurRadius = 4,
            ShadowDepth = 0,
            Opacity = 0.9,
            Color = Color.FromRgb(haloColor.R, haloColor.G, haloColor.B),
        };
        effect.Freeze();
        return effect;
    }

    /// <summary>
    /// 「#RRGGBB」等の色文字列をブラシに変換する(Freeze済み)。変換できなければ予備の色を使う。
    /// </summary>
    /// <param name="colorText">色の文字列(設定値の Effective〜 を渡す想定)。</param>
    /// <param name="fallback">変換できなかったときに使う色の文字列。</param>
    /// <returns>単色ブラシ。</returns>
    public static SolidColorBrush ToBrush(string colorText, string fallback)
    {
        var brush = new SolidColorBrush(ToColor(colorText, fallback));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 「#RRGGBB」等の色文字列を色に変換する。変換できなければ予備の色を使う。
    /// </summary>
    /// <param name="colorText">色の文字列。</param>
    /// <param name="fallback">変換できなかったときに使う色の文字列。</param>
    /// <returns>色。</returns>
    public static Color ToColor(string colorText, string fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(colorText)!;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            return (Color)ColorConverter.ConvertFromString(fallback)!;
        }
    }
}
