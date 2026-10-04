using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AgentDeskApp;

/// <summary>
/// ワークスペース直下に新しいチーム（サブプロジェクト）を作成するウィザード画面。
/// フォルダ作成とルールテンプレート（CLAUDE.md / GEMINI.md）の自動生成を一括して行う。
/// </summary>
public partial class CreateTeamWindow : Window
{
    private readonly string _parentGroupPath;

    /// <summary>作成されたチームフォルダの絶対パス（キャンセル時はnull）。</summary>
    public string? CreatedTeamPath { get; private set; }

    public CreateTeamWindow(string parentGroupPath, string parentGroupName)
    {
        InitializeComponent();
        _parentGroupPath = parentGroupPath;
        ParentGroupLabel.Text = $"親ワークスペース: {parentGroupName} ({parentGroupPath})";
        UpdatePreviewPath();
    }

    private void TeamNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreviewPath();
    }

    private void UpdatePreviewPath()
    {
        var rawName = TeamNameTextBox.Text.Trim();
        var safeName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars()));
        var previewPath = Path.Combine(_parentGroupPath, string.IsNullOrEmpty(safeName) ? "..." : safeName);
        FolderPathPreviewText.Text = $"パス: {previewPath}";
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "チームにするフォルダを選択",
            InitialDirectory = _parentGroupPath,
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedFolder = dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var expectedParent = _parentGroupPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(Path.GetDirectoryName(selectedFolder), expectedParent, StringComparison.OrdinalIgnoreCase))
            {
                TeamNameTextBox.Text = Path.GetFileName(selectedFolder);
            }
            else
            {
                MessageBox.Show(
                    this,
                    $"選択したフォルダは、親ワークスペース直下ではありません。\n親ワークスペース直下のフォルダを指定するか、チーム名を入力して新規作成してください。",
                    "フォルダ選択エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var teamName = TeamNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(teamName))
        {
            MessageBox.Show(this, "チーム名を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var safeName = string.Join("_", teamName.Split(Path.GetInvalidFileNameChars()));
        var targetDir = Path.Combine(_parentGroupPath, safeName);
        var mission = MissionTextBox.Text.Trim();

        try
        {
            // フォルダの作成
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // 親ワークスペースの memory/AI_MEMORY.md の存在確認＆自動初期化 (M-1)
            EnsureParentSharedMemory(_parentGroupPath);

            var parentMemoryPath = Path.Combine(_parentGroupPath, "memory", "AI_MEMORY.md");

            // CLAUDE.md の自動生成
            if (CreateClaudeMdCheck.IsChecked == true)
            {
                var claudeMdPath = Path.Combine(targetDir, "CLAUDE.md");
                if (!File.Exists(claudeMdPath))
                {
                    var lines = new List<string>
                    {
                        $"# {teamName}",
                        string.Empty,
                    };
                    if (!string.IsNullOrWhiteSpace(mission))
                    {
                        lines.Add($"## ミッション・役割");
                        lines.Add(mission);
                        lines.Add(string.Empty);
                    }
                    lines.Add("## AI共通記憶 (Shared Memory) の参照");
                    lines.Add($"- 会話開始時に、親ワークスペースの共通記憶目次（`{parentMemoryPath}`）を必ず確認すること。");
                    lines.Add("- 新しい教訓や決定事項があれば、親の `memory/` 配下に記録し目次に追加すること。");
                    lines.Add(string.Empty);
                    lines.Add("## 作業方針");
                    lines.Add("- 品質と動作確認を徹底する");
                    lines.Add("- 変更時はこまめにテストとコミットを行う");
                    lines.Add(string.Empty);

                    File.WriteAllLines(claudeMdPath, lines);
                }
            }

            // GEMINI.md の自動生成
            if (CreateGeminiMdCheck.IsChecked == true)
            {
                var geminiMdPath = Path.Combine(targetDir, "GEMINI.md");
                if (!File.Exists(geminiMdPath))
                {
                    var lines = new List<string>
                    {
                        $"# {teamName} (Gemini / Antigravity連携)",
                        string.Empty,
                    };
                    if (!string.IsNullOrWhiteSpace(mission))
                    {
                        lines.Add($"## ミッション・役割");
                        lines.Add(mission);
                        lines.Add(string.Empty);
                    }
                    lines.Add("## AI共通記憶 (Shared Memory) の参照");
                    lines.Add($"- 会話開始時に、親ワークスペースの共通記憶目次（`{parentMemoryPath}`）を必ず確認すること。");
                    lines.Add("- 新しい教訓や決定事項があれば、親の `memory/` 配下に記録し目次に追加すること。");
                    lines.Add(string.Empty);
                    lines.Add("## 作業方針");
                    lines.Add("- ターミナルコマンドや重要操作の前には日本語で事前説明を行う");
                    lines.Add("- 質問・判断は選択式（2〜4択）で提示する");
                    lines.Add(string.Empty);

                    File.WriteAllLines(geminiMdPath, lines);
                }
            }

            CreatedTeamPath = targetDir;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"チーム作成中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void EnsureParentSharedMemory(string parentGroupPath)
    {
        try
        {
            var memoryDir = Path.Combine(parentGroupPath, "memory");
            var memoryIndexFile = Path.Combine(memoryDir, "AI_MEMORY.md");

            if (!Directory.Exists(memoryDir))
            {
                Directory.CreateDirectory(memoryDir);
            }

            if (!File.Exists(memoryIndexFile))
            {
                var template =
@"# AI共通記憶 (Shared AI Memory) — 目次

このファイルおよび同ディレクトリ内のファイルは、**Claude Code・Gemini (Antigravity)・その他すべてのAIアシスタントが共通で読み書きする「共有記憶」**です。

## 運用ルール (全AI必須)
1. **会話開始時**: 必ずこの目次ファイルを読み、会話に関連するトピックがあれば該当ファイル（`*.md`）を追加で参照すること。
2. **会話終了・区切り時**: 新しい教訓、ユーザーの好み、重要な設計方針やプロジェクトの進捗・決定事項が出た場合、同ディレクトリ内にファイルを作成または更新し、この目次（`AI_MEMORY.md`）に1行追加すること。

---

## 記憶インデックス (トピック別ファイル一覧)
";
                File.WriteAllText(memoryIndexFile, template);
            }
        }
        catch
        {
            // 共通記憶の自動生成失敗はチーム自体の作成を妨げないよう握りつぶす
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
