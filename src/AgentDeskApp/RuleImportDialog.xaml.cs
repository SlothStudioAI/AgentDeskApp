using System.Windows;
using System.Windows.Threading;

namespace AgentDeskApp;

/// <summary>
/// ルール移植の実行方式（スマートマージまたは完全上書き）。
/// </summary>
public enum RuleImportMode
{
    SmartMerge,
    Overwrite,
}

/// <summary>
/// CLAUDE.md と GEMINI.md の相互移植オプションを選択するダイアログ。
/// </summary>
public partial class RuleImportDialog : Window
{
    private readonly string _sourceName;
    private readonly string _targetName;
    private readonly string _sourceContent;
    private readonly string _targetContent;
    private readonly DispatcherTimer _noticeTimer;

    public RuleImportMode SelectedMode { get; private set; } = RuleImportMode.SmartMerge;
    public bool CreateBackup { get; private set; } = true;

    public RuleImportDialog(string sourceName, string targetName, string sourceContent, string targetContent)
    {
        InitializeComponent();

        _sourceName = sourceName;
        _targetName = targetName;
        _sourceContent = sourceContent;
        _targetContent = targetContent;

        HeaderTitleText.Text = $"🔄 ルールの相互移植 ({_sourceName} ➔ {_targetName})";
        HeaderDescText.Text = $"「{_sourceName}」の内容を、編集中の「{_targetName}」へ取り込みます。";

        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _noticeTimer.Tick += (s, e) =>
        {
            CopyNoticeText.Visibility = Visibility.Collapsed;
            _noticeTimer.Stop();
        };
    }

    private void CopyAiPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var prompt =
$@"以下の2つのAIルールファイル（{_sourceName} と {_targetName}）の内容を比較・分析し、重複や矛盾をきれいに解消・統合して、{_targetName} の新しいルール構成を作成してください。

---
### 【取り込み元 ({_sourceName})】
{_sourceContent.Trim()}

---
### 【現在の対象 ({_targetName})】
{_targetContent.Trim()}
---

【指示】
1. 共通する作業方針や理念は1つにまとめ、重複した記述を排除してください。
2. 矛盾するルールがある場合は、より安全で具体的な指示を優先してください。
3. {_targetName} の形式に沿ったMarkdownとして出力してください。";

        Clipboard.SetText(prompt);
        CopyNoticeText.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = SmartMergeRadio.IsChecked == true ? RuleImportMode.SmartMerge : RuleImportMode.Overwrite;
        CreateBackup = BackupCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
