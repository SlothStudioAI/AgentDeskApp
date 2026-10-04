using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgentDeskApp;

/// <summary>
/// エージェント定義の新規作成・編集を行うフォーム画面。
/// 保存先(部/チームのどちら向けの追加ボタンから開いたか)は呼び出し元が既に決めているため、
/// この画面自体にスコープ選択のUIは持たない。
/// 保存時、frontmatter+本文の形式で.mdファイルを書き出す(<see cref="AgentDefinitionLoader.Save"/>)。
/// </summary>
public partial class AgentEditWindow : Window
{
    /// <summary>
    /// ツールのチェックボックス一覧に出す、よく使う組み込みツール名(公式ドキュメントで確認済みのもの)。
    /// 表示は日本語+英語名の併記(U-10)、保存される値(Tag)は従来通り英語名のまま。
    /// 一覧に無いツール(MCPツール名等)は<see cref="ExtraToolsTextBox"/>で自由入力する。
    /// </summary>
    /// <summary>ツール選択チェックボックス1つ分の幅(px)。固定幅にして折り返し時も列をそろえる。</summary>
    private const double ToolCheckBoxWidth = 250;

    private static readonly (string Value, string Label)[] CommonTools =
    [
        ("Bash", "💻 ターミナル操作 (Bash)"),
        ("Read", "📄 ファイル読み込み (Read)"),
        ("Edit", "✏️ ファイル編集 (Edit)"),
        ("Write", "📝 ファイル新規作成 (Write)"),
        ("Glob", "🔍 ファイル検索 (Glob)"),
        ("Grep", "🔎 文字列検索 (Grep)"),
        ("WebFetch", "🌐 URL取得 (WebFetch)"),
        ("WebSearch", "🔭 Web検索 (WebSearch)"),
        ("NotebookEdit", "📓 ノートブック編集 (NotebookEdit)"),
        ("TodoWrite", "✅ タスク管理 (TodoWrite)"),
        ("Agent", "🤝 サブエージェント呼び出し (Agent)"),
    ];

    /// <summary>
    /// 「モデル」欄の選択肢を目的別ラベルで提示する(U-10)。IsEditable=Trueのため一覧に無い値も入力できる。
    /// </summary>
    private static readonly (string Value, string Label)[] ModelOptions =
    [
        ("inherit", "🔄 継承 (呼び出し元と同じモデル) (inherit)"),
        ("haiku", "🏃 単純作業・高速 (haiku)"),
        ("sonnet", "⚡ 標準バランス (sonnet)"),
        ("opus", "🧠 じっくり深く考える (opus)"),
    ];

    /// <summary>ComboBoxのテキストからモデル値を取り出す(ラベル選択時は末尾の"(値)"を、それ以外は入力値をそのまま使う)。</summary>
    private static string ResolveModelValue(string rawText)
    {
        var match = Regex.Match(rawText, @"\(([a-z0-9_.\-]+)\)\s*$");
        if (match.Success && ModelOptions.Any(m => m.Value == match.Groups[1].Value))
        {
            return match.Groups[1].Value;
        }

        return rawText;
    }

    /// <summary>保存済みのモデル値(例: "opus")を、対応する目的別ラベルに変換する(該当が無ければそのまま表示)。</summary>
    private static string ToModelLabel(string? rawModel)
    {
        if (string.IsNullOrWhiteSpace(rawModel))
        {
            return string.Empty;
        }

        foreach (var (value, label) in ModelOptions)
        {
            if (string.Equals(value, rawModel, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }

        return rawModel;
    }

    /// <summary>表示名から識別子(半角英数字・ハイフン)を自動生成する(U-10)。日本語等の非ASCII文字は除去される。</summary>
    private static string GenerateSlug(string displayName)
    {
        var sb = new StringBuilder();
        var lastWasHyphen = true; // 先頭のハイフンを防ぐ
        foreach (var ch in displayName.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        return sb.ToString().TrimEnd('-');
    }

    /// <summary>
    /// 「モデル(Gemini)」欄の選択肢。Antigravityのinvoke_subagentで指定できる値(U-23)。
    /// IsEditable=Trueのため一覧に無い値も入力できる。
    /// </summary>
    private static readonly (string Value, string Label)[] GeminiModelOptions =
    [
        ("inherit", "🔄 継承 (呼び出し元と同じモデル) (inherit)"),
        ("flash_lite", "⚡ 超軽量・最速 (分類・ラベル付け向け) (flash_lite)"),
        ("flash", "🚀 高速・低コスト (探索・調査向け、標準) (flash)"),
        ("pro", "🧠 最高知能 (複雑な設計・深い推論向け) (pro)"),
    ];

    /// <summary>Gemini用ComboBoxのテキストからモデル値を取り出す(<see cref="ResolveModelValue"/>のGemini版)。</summary>
    private static string ResolveGeminiModelValue(string rawText)
    {
        var match = Regex.Match(rawText, @"\(([a-z0-9_.\-]+)\)\s*$");
        if (match.Success && GeminiModelOptions.Any(m => m.Value == match.Groups[1].Value))
        {
            return match.Groups[1].Value;
        }

        return rawText;
    }

    /// <summary>保存済みのGeminiモデル値を、対応する目的別ラベルに変換する(<see cref="ToModelLabel"/>のGemini版)。</summary>
    private static string ToGeminiModelLabel(string? rawModel)
    {
        if (string.IsNullOrWhiteSpace(rawModel))
        {
            return string.Empty;
        }

        foreach (var (value, label) in GeminiModelOptions)
        {
            if (string.Equals(value, rawModel, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }

        return rawModel;
    }

    /// <summary>「色」欄の選択肢(よく使う色名)。IsEditable=Trueのため一覧に無い色名・16進数値も入力できる。</summary>
    private static readonly string[] ColorOptions =
    [
        "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple", "Pink", "Brown", "Gray",
    ];

    private readonly AgentDefinition? _editingAgent;
    private readonly string _targetDir;
    private readonly Func<IReadOnlyList<StudioAgentEntry>>? _studioAgentsProvider;
    private string? _currentAvatarPath;
    private bool _avatarRemoved;

    /// <summary>「標準の画像に戻す」を押した状態ならtrue。保存時にチームフォルダ内の個別画像を整理する。</summary>
    private bool _restoredToBundled;
    private bool _nameManuallyEdited;
    private bool _settingNameProgrammatically;

    /// <summary>保存が完了したらtrue。呼び出し元はこれを見て一覧の再読み込みを行う。</summary>
    public bool Saved { get; private set; }

    /// <summary>
    /// 新規作成・編集どちらの画面としても使えるコンストラクタ。
    /// </summary>
    /// <param name="editingAgent">編集対象。新規作成の場合はnull。</param>
    /// <param name="targetDir">
    /// 新規作成時の保存先フォルダ(呼び出し元の「＋メンバー追加」ボタンが属する部/チームのエージェント定義フォルダ)。
    /// 編集時は<paramref name="editingAgent"/>自身のファイルパスをそのまま使うため参照されない。
    /// </param>
    /// <param name="targetLabel">画面上部に表示する保存先の説明(例: "○○部に追加"、"チームXに追加")。</param>
    /// <param name="studioAgentsProvider">
    /// スタジオ全体のメンバー一覧を返す関数。保存時に呼び出して、IdおよびDisplayName(呼び名)の重複を検出する(BUG-18)。
    /// 省略時は重複チェックを行わない。
    /// </param>
    public AgentEditWindow(
        AgentDefinition? editingAgent,
        string targetDir,
        string targetLabel,
        Func<IReadOnlyList<StudioAgentEntry>>? studioAgentsProvider = null)
    {
        InitializeComponent();
        _editingAgent = editingAgent;
        _targetDir = targetDir;
        _studioAgentsProvider = studioAgentsProvider;

        Title = editingAgent is null ? "メンバーを追加" : $"メンバーを編集: {editingAgent.EffectiveDisplayName}";
        TargetText.Text = editingAgent is null ? targetLabel : editingAgent.FilePath;

        ModelComboBox.ItemsSource = ModelOptions.Select(m => m.Label).ToArray();
        GeminiModelComboBox.ItemsSource = GeminiModelOptions.Select(m => m.Label).ToArray();
        ColorComboBox.ItemsSource = ColorOptions;

        var existingTools = (editingAgent?.Tools ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        foreach (var (value, label) in CommonTools)
        {
            ToolsCheckPanel.Children.Add(new CheckBox
            {
                Content = label,
                Tag = value,
                IsChecked = existingTools.Contains(value),
                Width = ToolCheckBoxWidth,
                Margin = new Thickness(0, 0, 8, 4),
            });
        }

        ExtraToolsTextBox.Text = string.Join(", ", existingTools.Except(CommonTools.Select(t => t.Value)));

        if (editingAgent is not null)
        {
            NameTextBox.Text = editingAgent.Name;
            NameTextBox.IsEnabled = false; // 名前(ファイル名の元)は編集時は変更不可(v1では対象外)。
            DisplayNameTextBox.Text = editingAgent.DisplayName ?? string.Empty;
            DescriptionTextBox.Text = editingAgent.Description;
            // Claude側モデルは、単独GeminiならSKILL.mdの補助キー(ClaudeModel)に退避されている
            ModelComboBox.Text = ToModelLabel(editingAgent.Engine == AgentEngineKind.Gemini
                ? editingAgent.ClaudeModel
                : editingAgent.Model);
            ColorComboBox.Text = editingAgent.Color ?? string.Empty;
            BodyTextBox.Text = editingAgent.Body;

            // Gemini側モデルは、単独GeminiならModelに、SharedならGeminiModelに入っている (U-23)
            GeminiModelComboBox.Text = editingAgent.Engine == AgentEngineKind.Gemini
                ? ToGeminiModelLabel(editingAgent.Model)
                : ToGeminiModelLabel(editingAgent.GeminiModel);

            // 既存エージェントのエンジンに応じた配備先初期選択
            switch (editingAgent.Engine)
            {
                case AgentEngineKind.Claude:
                    DeployClaudeRadio.IsChecked = true;
                    break;
                case AgentEngineKind.Gemini:
                    DeployGeminiRadio.IsChecked = true;
                    break;
                case AgentEngineKind.Shared:
                default:
                    DeploySharedRadio.IsChecked = true;
                    break;
            }
        }

        // 配備先ラジオに応じてGeminiモデル欄の表示を切り替える (U-23)
        // 配備先の変更で「標準の画像に戻す」ボタンの表示も切り替える(同梱画像はClaude側の定義でのみ表示されるため)
        DeploySharedRadio.Checked += (_, _) => { UpdateGeminiModelPanelVisibility(); UpdateRestoreBundledAvatarButtonVisibility(); };
        DeployClaudeRadio.Checked += (_, _) => { UpdateGeminiModelPanelVisibility(); UpdateRestoreBundledAvatarButtonVisibility(); };
        DeployGeminiRadio.Checked += (_, _) => { UpdateGeminiModelPanelVisibility(); UpdateRestoreBundledAvatarButtonVisibility(); };
        UpdateGeminiModelPanelVisibility();

        _currentAvatarPath = editingAgent?.AvatarPath;
        // 「画像なし」の印(avatar: none)があるメンバーは解除済みの状態で開く(そのまま保存しても印が残るように)
        _avatarRemoved = editingAgent?.AvatarDisabled == true;
        UpdateAvatarPreview();

        NameTextBox.TextChanged += (_, _) =>
        {
            // プログラムによる自動補完ではなく、ユーザー自身が入力した場合は以後の自動生成を止める (U-10)
            if (!_settingNameProgrammatically)
            {
                _nameManuallyEdited = true;
            }
            UpdateAvatarPreview();
        };
        DisplayNameTextBox.TextChanged += (_, _) =>
        {
            // 新規作成時、識別子が未編集なら表示名から自動生成する (U-10)
            if (_editingAgent is null && !_nameManuallyEdited)
            {
                _settingNameProgrammatically = true;
                NameTextBox.Text = GenerateSlug(DisplayNameTextBox.Text);
                _settingNameProgrammatically = false;
            }
            UpdateAvatarPreview();
        };
    }

    /// <summary>
    /// 配備先(Deployラジオ)に応じて、Claudeモデル/色の行とGeminiモデル行の表示を切り替える (U-23)。
    /// 非表示の欄の値は保持される(保存処理の仕様は従来どおり)。
    /// </summary>
    private void UpdateGeminiModelPanelVisibility()
    {
        var engine = DeployGeminiRadio.IsChecked == true ? AgentEngineKind.Gemini
            : DeployClaudeRadio.IsChecked == true ? AgentEngineKind.Claude
            : AgentEngineKind.Shared;
        var visibility = EditFieldVisibility.For(engine);
        // モデル(Claude)と色は同じ行に並ぶため、どちらも表示可の場合のみ行ごと表示する
        ClaudeModelPanel.Visibility = visibility.ClaudeModel || visibility.Color ? Visibility.Visible : Visibility.Collapsed;
        GeminiModelPanel.Visibility = visibility.GeminiModel ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 識別子(name)に対応するアプリ同梱アバター画像を探す。
    /// </summary>
    /// <returns>同梱画像の絶対パス。無ければ null。</returns>
    private string? FindBundledAvatarForCurrentName()
    {
        var name = NameTextBox.Text.Trim().Replace(' ', '-').ToLowerInvariant();
        return string.IsNullOrEmpty(name) ? null : AgentDefinitionLoader.FindBundledAvatar(name);
    }

    /// <summary>
    /// 「標準の画像に戻す」ボタンを、同梱画像があるメンバーで、かつ配備先にClaudeを含む場合だけ表示する。
    /// (Gemini単独のSKILL.mdは同梱画像を代替表示しないため、戻しても表示が変わらないので出さない)
    /// </summary>
    private void UpdateRestoreBundledAvatarButtonVisibility()
    {
        var includesClaude = DeployClaudeRadio.IsChecked == true || DeploySharedRadio.IsChecked == true;
        RestoreBundledAvatarButton.Visibility = includesClaude && FindBundledAvatarForCurrentName() is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// アバター欄のプレビュー(画像または背景色+頭文字)と状態表示を、現在の選択状態に合わせて更新する。
    /// </summary>
    private void UpdateAvatarPreview()
    {
        UpdateRestoreBundledAvatarButtonVisibility();

        if (!_avatarRemoved && !string.IsNullOrWhiteSpace(_currentAvatarPath) && File.Exists(_currentAvatarPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_currentAvatarPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                AvatarPreviewBorder.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                AvatarPreviewBorder.Child = null;
                AvatarStatusText.Text = AgentDefinitionLoader.IsBundledAvatarPath(_currentAvatarPath)
                    ? "🖼️ 標準の画像 (アプリ同梱)"
                    : $"🖼️ {Path.GetFileName(_currentAvatarPath)}";
                return;
            }
            catch
            {
                // 画像読み込み失敗時はフォールバック
            }
        }

        // 画像がない場合: メンバーカードと同じ灰青グラデーション + 頭文字(色設定には連動しない)
        AvatarPreviewBorder.Background = AvatarInitialStyle.CreateBackground();

        var name = !string.IsNullOrWhiteSpace(DisplayNameTextBox.Text)
            ? DisplayNameTextBox.Text.Trim()
            : (!string.IsNullOrWhiteSpace(NameTextBox.Text) ? NameTextBox.Text.Trim() : "?");
        var initial = AvatarInitialStyle.GetInitial(name);

        AvatarInitialText.Text = initial;
        AvatarInitialText.Foreground = Brushes.White;
        AvatarPreviewBorder.Child = AvatarInitialText;

        AvatarStatusText.Text = _avatarRemoved
            ? "アバター画像は解除されました (頭文字アイコン)"
            : "🖼️ 画像をここにドラッグ＆ドロップ";
    }

    private void AvatarDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            AvatarDropZone.Background = (Brush)FindResource("MenuItemHighlightBrush");
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void AvatarDropZone_DragLeave(object sender, DragEventArgs e)
    {
        AvatarDropZone.Background = (Brush)FindResource("PanelInsetBrush");
    }

    private void AvatarDropZone_Drop(object sender, DragEventArgs e)
    {
        AvatarDropZone.Background = (Brush)FindResource("PanelInsetBrush");
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var supported = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
            var imageFile = files?.FirstOrDefault(f => supported.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (imageFile != null)
            {
                _currentAvatarPath = imageFile;
                _avatarRemoved = false;
                _restoredToBundled = false;
                UpdateAvatarPreview();
            }
            else
            {
                MessageBox.Show(this, "対応している画像形式は PNG, JPG, WebP, BMP です。", "画像形式エラー", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void SelectAvatarButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "アバター画像を選択",
            Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|すべてのファイル (*.*)|*.*",
            Multiselect = false,
        };

        if (dlg.ShowDialog(this) == true)
        {
            _currentAvatarPath = dlg.FileName;
            _avatarRemoved = false;
            _restoredToBundled = false;
            UpdateAvatarPreview();
        }
    }

    /// <summary>
    /// 「×」ボタン: アバター画像を解除する。保存すると frontmatter に「画像なし」の印(avatar: none)が書かれ、
    /// 同梱画像も出さず頭文字表示になる。
    /// </summary>
    /// <param name="sender">イベント送信元。</param>
    /// <param name="e">イベント引数。</param>
    private void ClearAvatarButton_Click(object sender, RoutedEventArgs e)
    {
        _currentAvatarPath = null;
        _avatarRemoved = true;
        _restoredToBundled = false;
        UpdateAvatarPreview();
    }

    /// <summary>
    /// 「標準の画像に戻す」ボタン: プレビューをアプリ同梱画像にする。保存すると avatar を書かない状態に戻る
    /// (チームフォルダへのコピーはしない)。
    /// </summary>
    /// <param name="sender">イベント送信元。</param>
    /// <param name="e">イベント引数。</param>
    private void RestoreBundledAvatarButton_Click(object sender, RoutedEventArgs e)
    {
        var bundled = FindBundledAvatarForCurrentName();
        if (bundled is null)
        {
            return;
        }

        _currentAvatarPath = bundled;
        _avatarRemoved = false;
        _restoredToBundled = true;
        UpdateAvatarPreview();
    }

    private (string claudeDir, string geminiSkillsDir) ResolveTargetDirs()
    {
        string baseDir;
        var refPath = _editingAgent?.FilePath ?? _targetDir;
        var normalized = refPath.Replace('/', Path.DirectorySeparatorChar);

        var claudeAgentsIndex = normalized.LastIndexOf(Path.DirectorySeparatorChar + ".claude" + Path.DirectorySeparatorChar + "agents", StringComparison.OrdinalIgnoreCase);
        if (claudeAgentsIndex >= 0)
        {
            baseDir = normalized[..claudeAgentsIndex];
        }
        else
        {
            var agentsSkillsIndex = normalized.LastIndexOf(Path.DirectorySeparatorChar + ".agents" + Path.DirectorySeparatorChar + "skills", StringComparison.OrdinalIgnoreCase);
            if (agentsSkillsIndex >= 0)
            {
                baseDir = normalized[..agentsSkillsIndex];
            }
            else
            {
                baseDir = Directory.Exists(refPath) ? refPath : Path.GetDirectoryName(refPath) ?? refPath;
            }
        }

        var claudeDir = Path.Combine(baseDir, ".claude", "agents");
        var geminiSkillsDir = Path.Combine(baseDir, ".agents", "skills");
        return (claudeDir, geminiSkillsDir);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var rawName = NameTextBox.Text.Trim();
        var description = DescriptionTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(rawName) || string.IsNullOrWhiteSpace(description))
        {
            MessageBox.Show(this, "名前と説明は必須です。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 半角英数字・ハイフンへの正規化（空白をハイフンに、大文字を小文字に自動補正）
        var normalizedName = rawName.Replace(' ', '-').ToLowerInvariant();

        // 識別子に半角英数字・ハイフン・アンダースコア以外（日本語や特殊記号）が含まれていないかチェック
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalizedName, @"^[a-z0-9_-]+$"))
        {
            MessageBox.Show(
                this,
                $"名前(識別子)「{rawName}」に使用できない文字が含まれています。\n\n" +
                "名前(識別子)はCLIやエンジンから呼び出すキーとなるため、半角英小文字・数字・ハイフン（例: code-reviewer, helper）で入力してください。\n\n" +
                "日本語の名称を設定したい場合は、下の「表示名」に入力してください。",
                "名前(識別子)の入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var safeName = normalizedName;
        var (claudeDir, geminiSkillsDir) = ResolveTargetDirs();
        var claudeFilePath = Path.Combine(claudeDir, $"{safeName}.md");
        var geminiSkillDir = Path.Combine(geminiSkillsDir, safeName);

        // 新規作成時のみ重複チェック
        if (_editingAgent is null)
        {
            var existsInClaude = File.Exists(claudeFilePath);
            var existsInGemini = Directory.Exists(geminiSkillDir);
            if (existsInClaude || existsInGemini)
            {
                MessageBox.Show(
                    this,
                    $"「{safeName}」という名前のエージェント/スキルは既にこの場所に存在します。別の名前にしてください。",
                    "重複エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        // BUG-18: スタジオ全体で Id と DisplayName(呼び名) を一意にする。自分自身は比較から除外する。
        // 編集時はIdを変更できないため、既存データにIdの重複が残っていても呼び名の修正(救済)ができるようId比較は新規時のみ行う。
        if (_studioAgentsProvider is not null)
        {
            var newDisplayName = string.IsNullOrWhiteSpace(DisplayNameTextBox.Text) ? null : DisplayNameTextBox.Text.Trim();
            var conflict = AgentNameUniqueness.FindConflict(
                safeName, newDisplayName, _studioAgentsProvider(), _editingAgent, checkId: _editingAgent is null);
            if (conflict is not null)
            {
                MessageBox.Show(
                    this,
                    $"{conflict.ToMessage()}\n\nスタジオ内では同じ名前のメンバーは登録できません。別の名前に変更してください。",
                    "同名エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var checkedTools = ToolsCheckPanel.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag);
        var extraTools = ExtraToolsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tools = string.Join(", ", checkedTools.Concat(extraTools));

        var model = ResolveModelValue(ModelComboBox.Text.Trim());
        var geminiModel = ResolveGeminiModelValue(GeminiModelComboBox.Text.Trim());
        var color = ColorComboBox.Text.Trim();
        var body = BodyTextBox.Text;
        var displayName = string.IsNullOrWhiteSpace(DisplayNameTextBox.Text) ? null : DisplayNameTextBox.Text.Trim();

        var deployToClaude = DeployClaudeRadio.IsChecked == true || DeploySharedRadio.IsChecked == true;
        var deployToGemini = DeployGeminiRadio.IsChecked == true || DeploySharedRadio.IsChecked == true;

        try
        {
            // アバター画像の保存・削除処理
            // frontmatter の avatar に書く値(解除なら none、同梱画像なら書かない、選んだ画像ならコピー先のファイル名)
            var avatarFileName = AgentDefinitionLoader.ResolveAvatarFieldForSave(
                _avatarRemoved, _currentAvatarPath, safeName, _editingAgent?.AvatarPath);

            if (_avatarRemoved || _restoredToBundled)
            {
                // アバター削除(「標準の画像に戻す」の場合も、チームフォルダ内の個別画像が同梱画像より優先されないよう整理する)
                try
                {
                    var candidates = new[] {
                        Path.Combine(claudeDir, $"{safeName}.png"),
                        Path.Combine(claudeDir, $"{safeName}.jpg"),
                        Path.Combine(claudeDir, $"{safeName}.jpeg"),
                        Path.Combine(claudeDir, $"{safeName}.webp"),
                        Path.Combine(geminiSkillDir, $"{safeName}.png"),
                        Path.Combine(geminiSkillDir, $"avatar.png"),
                        Path.Combine(geminiSkillDir, $"{safeName}.jpg"),
                        Path.Combine(geminiSkillDir, $"avatar.jpg"),
                    };
                    foreach (var file in candidates)
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                }
                catch { }
            }
            else if (AgentDefinitionLoader.IsBundledAvatarPath(_currentAvatarPath))
            {
                // 同梱画像の代替表示のまま保存する場合は、チームフォルダへコピーせず avatar も書かない
                // (表示時にローダーが同梱画像を参照するため、見た目は変わらない)
            }
            else if (!string.IsNullOrWhiteSpace(_currentAvatarPath) && File.Exists(_currentAvatarPath))
            {
                // 配置先(Claude/Gemini/Shared)に応じて移行先へ確実にコピーする(コピー元は消さない)
                var avatarEngine = (deployToClaude, deployToGemini) switch
                {
                    (true, true) => AgentEngineKind.Shared,
                    (false, true) => AgentEngineKind.Gemini,
                    _ => AgentEngineKind.Claude,
                };
                AgentDeployWriter.CopyAvatarToTargets(
                    AgentDeployWriter.Plan(claudeDir, geminiSkillsDir, safeName, avatarEngine),
                    claudeDir, safeName, _currentAvatarPath);
            }

            // 配置先に応じた書き出し(Claude/Gemini/Shared)は共通クラスに委譲する(テンプレート追加画面と同じ処理)
            var engine = (deployToClaude, deployToGemini) switch
            {
                (true, true) => AgentEngineKind.Shared,
                (false, true) => AgentEngineKind.Gemini,
                _ => AgentEngineKind.Claude,
            };
            var plan = AgentDeployWriter.Plan(claudeDir, geminiSkillsDir, safeName, engine);
            AgentDeployWriter.Write(
                plan, claudeDir, geminiSkillsDir, safeName, description, tools, model, geminiModel,
                color, body, displayName, avatarFileName, removeUndeployedSide: true);

            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"メンバーの保存中にエラーが発生しました:\n{ex.Message}",
                "保存エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
