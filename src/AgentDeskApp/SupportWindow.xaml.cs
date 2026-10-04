using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentDeskApp;

/// <summary>
/// サイドバーの「Support」から開くヘルプ画面。本文は <see cref="SupportContent"/> から取得して表示する。
/// </summary>
public partial class SupportWindow : Window
{
    private const string HeadingColor = "#2F6B45";
    private const string BodyColor = "#3B2A1E";
    private const double HeadingFontSize = 14.5;
    private const double BodyFontSize = 12.5;
    private const double CopiedDisplayMilliseconds = 1500;

    /// <summary>
    /// ヘルプ画面を作る。
    /// </summary>
    /// <param name="owner">親ウィンドウ(メイン画面)。中央に表示する。配色・角丸は共有リソース(Themes)から参照する。</param>
    public SupportWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        Title = SupportContent.WindowTitle;
        HeaderTextBlock.Text = SupportContent.HeaderText;
        CloseButton.Content = SupportContent.CloseButtonText;

        BuildSections(SupportContent.Build(SupportContent.GetCurrentVersion(), AppSettingsLoader.SettingsFilePath, DiagnosticLog.DefaultPath));
    }

    /// <summary>
    /// セクション一覧から見出し・本文・コピー用欄を組み立てて画面に並べる。
    /// </summary>
    /// <param name="sections">表示するセクション。</param>
    private void BuildSections(IReadOnlyList<SupportSection> sections)
    {
        var converter = new BrushConverter();
        foreach (var section in sections)
        {
            SectionsPanel.Children.Add(new TextBlock
            {
                Text = section.Heading,
                FontSize = HeadingFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)converter.ConvertFromString(HeadingColor)!,
                Margin = new Thickness(0, SectionsPanel.Children.Count == 0 ? 0 : 18, 0, 6),
            });

            if (!string.IsNullOrEmpty(section.Body))
            {
                SectionsPanel.Children.Add(new TextBlock
                {
                    Text = section.Body,
                    FontSize = BodyFontSize,
                    Foreground = (Brush)converter.ConvertFromString(BodyColor)!,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20,
                });
            }

            if (section.CopyableText is { } copyable)
            {
                SectionsPanel.Children.Add(CreateCopyRow(copyable));
            }

            foreach (var item in section.Links ?? Array.Empty<SupportLinkItem>())
            {
                SectionsPanel.Children.Add(CreateLinkRow(item, converter));
            }
        }
    }

    /// <summary>
    /// 案内項目の1行を作る。リンク先があればクリックで既定ブラウザを開くリンク、無ければ案内文のみ。
    /// </summary>
    /// <param name="item">案内項目。</param>
    /// <param name="converter">色変換用。</param>
    /// <returns>組み立てた行。</returns>
    private static UIElement CreateLinkRow(SupportLinkItem item, BrushConverter converter)
    {
        var block = new TextBlock
        {
            FontSize = BodyFontSize,
            Foreground = (Brush)converter.ConvertFromString(BodyColor)!,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 4, 0, 0),
        };
        block.Inlines.Add(new Run(item.Label + ": ") { FontWeight = FontWeights.SemiBold });
        if (item.HasLink)
        {
            var link = new Hyperlink(new Run(item.Text)) { NavigateUri = new Uri(item.Url!) };
            link.RequestNavigate += (_, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch
                {
                    // ブラウザを開けなかった場合は何もしない
                }

                e.Handled = true;
            };
            block.Inlines.Add(link);
        }
        else
        {
            block.Inlines.Add(new Run(item.Text));
        }

        return block;
    }

    /// <summary>
    /// 選択できるテキスト欄とコピーボタンの1行を作る。
    /// </summary>
    /// <param name="text">表示・コピーする文字列。</param>
    /// <returns>組み立てた行。</returns>
    private static UIElement CreateCopyRow(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8, 6, 8, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(box, 0);
        grid.Children.Add(box);

        var button = new Button
        {
            Content = SupportContent.CopyButtonText,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => CopyToClipboard(button, text);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    /// <summary>
    /// 文字列をクリップボードへコピーし、ボタン表示を一時的に「コピーしました」にする。
    /// </summary>
    /// <param name="button">押されたボタン。</param>
    /// <param name="text">コピーする文字列。</param>
    private static void CopyToClipboard(Button button, string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // クリップボードが他アプリに使用中の場合は何もしない(欄の文字は手動で選択してコピーできる)
            return;
        }

        button.Content = SupportContent.CopiedButtonText;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CopiedDisplayMilliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = SupportContent.CopyButtonText;
        };
        timer.Start();
    }

    /// <summary>Escキーで閉じる。</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>閉じるボタン。</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
