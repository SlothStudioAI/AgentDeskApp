using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AgentDeskApp;

/// <summary>
/// チームやワークスペースの共通ルール(CLAUDE.md / GEMINI.md)をインライン閲覧・編集・保存できるウィンドウ。
/// </summary>
public partial class RulePopupWindow : Window
{
    private readonly string _claudeMdPath;
    private readonly string _geminiMdPath;
    private readonly DispatcherTimer _noticeTimer;
    private readonly Action? _openParentRule;

    /// <summary>
    /// 新しい <see cref="RulePopupWindow"/> クラスのインスタンスを初期化します。
    /// </summary>
    /// <param name="title">見出し(例: "部署のルール"、"チームのルール")。</param>
    /// <param name="claudeMdPath">表示するCLAUDE.mdの絶対パス。</param>
    /// <param name="geminiMdPath">表示するGEMINI.mdの絶対パス。省略時はclaudeMdPathと同一フォルダ内のGEMINI.mdを探索します。</param>
    /// <param name="parentRuleLinkLabel">
    /// 親スコープ（グループ/グローバル）のルールを見るボタンのラベル。nullの場合はボタンを表示しない (U-4)。
    /// </param>
    /// <param name="openParentRule">上記ボタンが押された時に実行する処理(親スコープの<see cref="RulePopupWindow"/>を開く等)。</param>
    public RulePopupWindow(
        string title,
        string claudeMdPath,
        string? geminiMdPath = null,
        string? parentRuleLinkLabel = null,
        Action? openParentRule = null)
    {
        InitializeComponent();

        HeaderText.Text = title;
        _claudeMdPath = claudeMdPath ?? string.Empty;
        _openParentRule = openParentRule;

        if (parentRuleLinkLabel is not null && openParentRule is not null)
        {
            ParentRuleButton.Content = parentRuleLinkLabel;
            ParentRuleButton.Visibility = Visibility.Visible;
        }

        // GEMINI.md のパスが指定されていない場合は、CLAUDE.md と同じディレクトリから推測
        if (string.IsNullOrWhiteSpace(geminiMdPath) && !string.IsNullOrWhiteSpace(_claudeMdPath))
        {
            var dir = Path.GetDirectoryName(_claudeMdPath);
            geminiMdPath = dir != null ? Path.Combine(dir, "GEMINI.md") : string.Empty;
        }
        _geminiMdPath = geminiMdPath ?? string.Empty;

        // 通知タイマー初期化
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _noticeTimer.Tick += (s, e) =>
        {
            SaveNoticeText.Visibility = Visibility.Collapsed;
            _noticeTimer.Stop();
        };

        LoadRuleContents();
    }

    private void LoadRuleContents()
    {
        // CLAUDE.md の読み込み
        ClaudeFilePathText.Text = string.IsNullOrWhiteSpace(_claudeMdPath) ? "(未設定)" : _claudeMdPath;
        if (!string.IsNullOrWhiteSpace(_claudeMdPath) && File.Exists(_claudeMdPath))
        {
            ClaudeContentTextBox.Text = File.ReadAllText(_claudeMdPath, Encoding.UTF8);
        }
        else
        {
            ClaudeContentTextBox.Text = "# プロジェクト共通ルール (CLAUDE.md)\n\nここにチームの作業方針やコーディング規約を記述します。\n";
        }

        // GEMINI.md の読み込み
        GeminiFilePathText.Text = string.IsNullOrWhiteSpace(_geminiMdPath) ? "(未設定)" : _geminiMdPath;
        if (!string.IsNullOrWhiteSpace(_geminiMdPath) && File.Exists(_geminiMdPath))
        {
            GeminiContentTextBox.Text = File.ReadAllText(_geminiMdPath, Encoding.UTF8);
        }
        else
        {
            GeminiContentTextBox.Text = "# プロジェクト共通ルール (GEMINI.md)\n\nここにAntigravity / Gemini向け作業方針や行動原則を記述します。\n";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveRules();
    }

    private void SaveRules()
    {
        try
        {
            // CLAUDE.md の保存
            if (!string.IsNullOrWhiteSpace(_claudeMdPath))
            {
                var dir = Path.GetDirectoryName(_claudeMdPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                AtomicFile.WriteAllText(_claudeMdPath, ClaudeContentTextBox.Text, Encoding.UTF8);
            }

            // GEMINI.md の保存
            if (!string.IsNullOrWhiteSpace(_geminiMdPath))
            {
                var dir = Path.GetDirectoryName(_geminiMdPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                AtomicFile.WriteAllText(_geminiMdPath, GeminiContentTextBox.Text, Encoding.UTF8);
            }

            ShowNotice("✅ ルールを保存しました！");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"ルールの保存中にエラーが発生しました:\n{ex.Message}", "保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowNotice(string message)
    {
        SaveNoticeText.Text = message;
        SaveNoticeText.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    // ---- 相互移植機能 (SmartMerge / Overwrite / Backup / Copy / External Import) ----

    private void CopyClaudeButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ClaudeContentTextBox.Text);
        ShowNotice("📋 CLAUDE.md の内容をクリップボードにコピーしました！");
    }

    private void CopyGeminiButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(GeminiContentTextBox.Text);
        ShowNotice("📋 GEMINI.md の内容をクリップボードにコピーしました！");
    }

    private void ImportToClaudeButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteImport("GEMINI.md", "CLAUDE.md", GeminiContentTextBox.Text, ClaudeContentTextBox, _claudeMdPath);
    }

    private void ImportToGeminiButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteImport("CLAUDE.md", "GEMINI.md", ClaudeContentTextBox.Text, GeminiContentTextBox, _geminiMdPath);
    }

    private void ImportFileToClaudeButton_Click(object sender, RoutedEventArgs e)
    {
        ImportFromExternalFile("CLAUDE.md", ClaudeContentTextBox, _claudeMdPath);
    }

    private void ImportFileToGeminiButton_Click(object sender, RoutedEventArgs e)
    {
        ImportFromExternalFile("GEMINI.md", GeminiContentTextBox, _geminiMdPath);
    }

    private void ImportFromExternalFile(string targetName, System.Windows.Controls.TextBox targetTextBox, string targetPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"取り込み元ファイルを選択 ➔ {targetName} へ反映",
            Filter = "Markdown ルール (*.md)|*.md|すべてのファイル (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var content = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                var sourceName = Path.GetFileName(dialog.FileName);
                ExecuteImport(sourceName, targetName, content, targetTextBox, targetPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"ファイルの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ExecuteImport(string sourceName, string targetName, string sourceContent, System.Windows.Controls.TextBox targetTextBox, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourceContent))
        {
            MessageBox.Show(this, $"取り込み元の「{sourceName}」の内容が空です。", "取り込み不可", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new RuleImportDialog(sourceName, targetName, sourceContent, targetTextBox.Text) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        // バックアップ作成
        if (dialog.CreateBackup)
        {
            try
            {
                CreateBackupFile(targetPath, targetTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"バックアップの作成に失敗しました:\n{ex.Message}\n\n移植は中断されました。", "バックアップエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (dialog.SelectedMode == RuleImportMode.Overwrite)
        {
            targetTextBox.Text = sourceContent;
            ShowNotice($"⚡ {sourceName} の内容で上書きしました！確認して「保存」してください。");
        }
        else
        {
            targetTextBox.Text = SmartMergeRules(targetTextBox.Text, sourceContent, sourceName);
            ShowNotice($"🧠 {sourceName} からマージしました！確認して「保存」してください。");
        }
    }

    private static string SmartMergeRules(string targetContent, string sourceContent, string sourceName)
    {
        var targetHeadings = ExtractHeadings(targetContent);
        var sourceSections = ExtractSections(sourceContent);

        var newSections = new List<string>();
        foreach (var (heading, body) in sourceSections)
        {
            var normalized = NormalizeHeading(heading);
            if (!targetHeadings.Contains(normalized))
            {
                newSections.Add(body.Trim());
            }
        }

        var sb = new StringBuilder();
        sb.Append(targetContent.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();

        if (newSections.Count > 0)
        {
            sb.AppendLine($"<!-- ▼ {sourceName} より移植されたルール（重複・矛盾がないかご確認ください） ▼ -->");
            foreach (var sec in newSections)
            {
                sb.AppendLine(sec);
                sb.AppendLine();
            }
            sb.AppendLine("<!-- ▲ 移植ここまで ▲ -->");
        }
        else
        {
            sb.AppendLine($"<!-- ▼ {sourceName} より移植（既存の見出しと類似していたため念のため全体を追記） ▼ -->");
            sb.AppendLine(sourceContent.Trim());
            sb.AppendLine("<!-- ▲ 移植ここまで ▲ -->");
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static HashSet<string> ExtractHeadings(string markdown)
    {
        var headings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                headings.Add(NormalizeHeading(trimmed));
            }
        }
        return headings;
    }

    private static string NormalizeHeading(string heading)
    {
        var withoutHash = heading.TrimStart('#').Trim();
        return new string(withoutHash.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());
    }

    private static List<(string Heading, string FullContent)> ExtractSections(string markdown)
    {
        var sections = new List<(string Heading, string FullContent)>();
        using var reader = new StringReader(markdown);
        string? line;
        string? currentHeading = null;
        var currentBody = new StringBuilder();

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                if (currentHeading != null)
                {
                    sections.Add((currentHeading, currentBody.ToString()));
                    currentBody.Clear();
                }
                currentHeading = trimmed;
            }

            if (currentHeading != null)
            {
                currentBody.AppendLine(line);
            }
        }

        if (currentHeading != null && currentBody.Length > 0)
        {
            sections.Add((currentHeading, currentBody.ToString()));
        }

        return sections;
    }

    // ---- バックアップ手動保存 ----

    private void BackupClaudeButton_Click(object sender, RoutedEventArgs e)
    {
        BackupRuleFile(_claudeMdPath, ClaudeContentTextBox.Text);
    }

    private void BackupGeminiButton_Click(object sender, RoutedEventArgs e)
    {
        BackupRuleFile(_geminiMdPath, GeminiContentTextBox.Text);
    }

    private void BackupRuleFile(string filePath, string content)
    {
        try
        {
            var backupPath = CreateBackupFile(filePath, content);
            if (!string.IsNullOrEmpty(backupPath))
            {
                ShowNotice($"📦 バックアップを作成しました: {Path.GetFileName(backupPath)}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"バックアップの作成に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string CreateBackupFile(string filePath, string content)
    {
        var dir = !string.IsNullOrWhiteSpace(filePath) ? Path.GetDirectoryName(filePath) : null;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            dir = Path.GetTempPath();
        }

        var baseName = !string.IsNullOrWhiteSpace(filePath) ? Path.GetFileName(filePath) : "rules.md";
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(dir, $"{baseName}.bak_{timestamp}");

        File.WriteAllText(backupPath, content, Encoding.UTF8);
        return backupPath;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        // 現在アクティブなタブ（0: CLAUDE.md, 1: GEMINI.md）に応じて開く対象フォルダを切り替える
        var isGeminiTab = RuleTabControl?.SelectedIndex == 1;
        var targetPath = isGeminiTab
            ? (!string.IsNullOrWhiteSpace(_geminiMdPath) ? _geminiMdPath : _claudeMdPath)
            : (!string.IsNullOrWhiteSpace(_claudeMdPath) ? _claudeMdPath : _geminiMdPath);

        var dir = !string.IsNullOrWhiteSpace(targetPath) ? Path.GetDirectoryName(targetPath) : null;

        if (!string.IsNullOrEmpty(dir))
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"フォルダを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            MessageBox.Show(this, "対象フォルダのパスが特定できませんでした。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SaveRules();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ParentRuleButton_Click(object sender, RoutedEventArgs e)
    {
        _openParentRule?.Invoke();
    }
}
