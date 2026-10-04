using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AgentDeskApp;

/// <summary>
/// アバター画像が無いときの「頭文字アイコン」の見た目(背景の灰青グラデーション・頭文字の取り方)を決める共通ロジック。
/// メンバーカードと編集画面のアバター欄で同じ見た目にそろえるために共有する。
/// 色設定(frontmatter の color)は Claude Code 側でだけ使う項目のため、ここでは一切参照しない。
/// </summary>
public static class AvatarInitialStyle
{
    /// <summary>グラデーションの左上の色(明るい灰青)。</summary>
    public const string StartColorHex = "#94A3B8";

    /// <summary>グラデーションの右下の色(濃い灰青)。</summary>
    public const string EndColorHex = "#475569";

    /// <summary>
    /// 頭文字アイコンの背景(灰青のグラデーション)を作る。色設定には依存しない。
    /// </summary>
    /// <returns>Freeze済みのグラデーション。</returns>
    public static LinearGradientBrush CreateBackground()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(StartColorHex)!, 0.0));
        // (右下の濃い色はカード下部では白っぽい下地に覆われる)
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(EndColorHex)!, 1.0));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 表示名から頭文字(先頭の1文字。絵文字などのサロゲートペアも1文字として扱う)を取り出す。
    /// </summary>
    /// <param name="displayName">表示名。空なら「?」。</param>
    /// <returns>頭文字。</returns>
    public static string GetInitial(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var trimmed = displayName.Trim();
        var firstElementLength = StringInfo.GetNextTextElementLength(trimmed);
        return firstElementLength <= 0 ? "?" : trimmed[..firstElementLength];
    }
}
