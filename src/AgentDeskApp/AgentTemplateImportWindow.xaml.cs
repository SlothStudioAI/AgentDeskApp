using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AgentDeskApp;

/// <summary>
/// 職種別エージェントテンプレートを選択し、チームへ一括追加するウィンドウ。
/// </summary>
public partial class AgentTemplateImportWindow : Window
{
    /// <summary>注記: Claude専用で追加する場合の文言。</summary>
    private const string NoteClaude = "ℹ️ Claudeエージェント(.claude/agents)として追加されます。";

    /// <summary>注記: Gemini専用で追加する場合の文言。</summary>
    private const string NoteGemini = "ℹ️ Geminiスキル(.agents/skills/<id>/SKILL.md)として追加されます。モデルは選択した段階に対応するGeminiモデルになります。";

    /// <summary>注記: 両方(Shared)で追加する場合の文言。</summary>
    private const string NoteShared = "ℹ️ ClaudeとGeminiの両方(Shared)に追加されます。モデルは選択した段階に対応するClaude/Geminiモデルがそれぞれ設定されます。";

    /// <summary>行のモデル選択ドロップダウンの幅(段階名「高速・低コスト(標準バランス)」が収まる幅)。</summary>
    private const double ModelComboWidth = 172;

    /// <summary>保存先表示の接頭辞。</summary>
    private const string SavePathLabelPrefix = "保存先: ";

    /// <summary>Claudeフォルダ(.claude/agents)から.agents/skillsを導くときのフォルダ名。</summary>
    private const string AgentsFolderName = ".agents";

    /// <summary>Geminiスキル置き場のサブフォルダ名。</summary>
    private const string SkillsFolderName = "skills";

    /// <summary>同梱アバター画像のフォルダ(実行ファイル基準の相対パス)。</summary>
    private static readonly string[] BundledAvatarRelativePath = ["Assets", "Avatars"];

    private readonly string _targetAgentDir;
    private readonly string _targetGeminiSkillsDir;
    private readonly bool _initialized;
    private readonly string _targetScopeName;
    private readonly List<AgentTemplateCategory> _categories;
    private AgentTemplateCategory? _currentCategory;

    private readonly Func<IReadOnlyList<StudioAgentEntry>>? _studioAgentsProvider;

    /// <summary>全スタジオのメンバー一覧を返す関数(追加先以外のスコープの同Idメンバー検出用)。</summary>
    private readonly Func<IReadOnlyList<StudioAgentEntry>>? _allStudioAgentsProvider;

    /// <summary>追加先以外のスコープに同Idのメンバーがいるテンプレート(テンプレート → 所属ラベル)。追加は妨げない。</summary>
    private readonly Dictionary<AgentTemplateItem, IReadOnlyList<string>> _otherScopeLabels = [];

    /// <summary>スタジオ内に登録済みのテンプレート(Id → 衝突内容)。チェック不可にして二重インポートを防ぐ(BUG-18)。</summary>
    private readonly Dictionary<AgentTemplateItem, NameConflict> _registered = [];

    public int ImportedCount { get; private set; }

    /// <param name="targetAgentDir">取り込み先の.claude/agentsフォルダ。</param>
    /// <param name="targetScopeName">取り込み先の表示名。</param>
    /// <param name="studioAgentsProvider">
    /// スタジオ全体のメンバー一覧を返す関数。IdまたはDisplayName(呼び名)が登録済みのテンプレートを取込不可にする(BUG-18)。
    /// 省略時は登録済み判定を行わない。
    /// </param>
    /// <param name="targetGeminiSkillsDir">取り込み先の.agents/skillsフォルダ。省略時は<paramref name="targetAgentDir"/>と同じ階層から導く。</param>
    /// <param name="allStudioAgentsProvider">
    /// 全スタジオ(全チーム・ワークスペース直下・共通)のメンバー一覧を返す関数。
    /// 追加先以外に同じIdのメンバーがいる場合、行に案内を出す(追加は妨げない)。省略時は案内を出さない。
    /// </param>
    public AgentTemplateImportWindow(
        string targetAgentDir,
        string targetScopeName,
        Func<IReadOnlyList<StudioAgentEntry>>? studioAgentsProvider = null,
        string? targetGeminiSkillsDir = null,
        Func<IReadOnlyList<StudioAgentEntry>>? allStudioAgentsProvider = null)
    {
        InitializeComponent();

        _targetAgentDir = targetAgentDir;
        _targetGeminiSkillsDir = targetGeminiSkillsDir ?? DeriveGeminiSkillsDir(targetAgentDir);
        _targetScopeName = targetScopeName;
        _studioAgentsProvider = studioAgentsProvider;
        _allStudioAgentsProvider = allStudioAgentsProvider;

        TargetScopeText.Text = _targetScopeName;
        _initialized = true;
        UpdateDeployDisplay();

        _categories = AgentTemplateRepository.GetCategories();
        MarkRegisteredTemplates();
        MarkOtherScopeTemplates();
        CategoryComboBox.ItemsSource = _categories;

        if (_categories.Count > 0)
        {
            CategoryComboBox.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// .claude/agentsフォルダと同じ階層の.agents/skillsフォルダを求める。
    /// </summary>
    /// <param name="claudeAgentsDir">.claude/agentsフォルダの絶対パス。</param>
    private static string DeriveGeminiSkillsDir(string claudeAgentsDir)
    {
        var claudeRoot = Path.GetDirectoryName(Path.GetFullPath(claudeAgentsDir).TrimEnd(Path.DirectorySeparatorChar));
        var baseDir = Path.GetDirectoryName(claudeRoot) ?? claudeAgentsDir;
        return Path.Combine(baseDir, AgentsFolderName, SkillsFolderName);
    }

    /// <summary>選択中の配置先(Claude / Gemini / Shared)を返す。</summary>
    private AgentEngineKind SelectedEngine =>
        DeploySharedRadio.IsChecked == true ? AgentEngineKind.Shared
        : DeployGeminiRadio.IsChecked == true ? AgentEngineKind.Gemini
        : AgentEngineKind.Claude;

    /// <summary>配置先ラジオの変更時に、保存先表示と注記を更新する。</summary>
    private void DeployRadio_Checked(object sender, RoutedEventArgs e) => UpdateDeployDisplay();

    /// <summary>選択中の配置先に応じて、保存先パス表示と注記を更新する。</summary>
    private void UpdateDeployDisplay()
    {
        if (!_initialized) return;

        var engine = SelectedEngine;
        TargetFolderPathText.Text = engine switch
        {
            AgentEngineKind.Gemini => $"{SavePathLabelPrefix}{_targetGeminiSkillsDir}",
            AgentEngineKind.Shared => $"{SavePathLabelPrefix}{_targetAgentDir} / {_targetGeminiSkillsDir}",
            _ => $"{SavePathLabelPrefix}{_targetAgentDir}",
        };
        TargetFolderPathText.ToolTip = TargetFolderPathText.Text;
        DeployNoteText.Text = engine switch
        {
            AgentEngineKind.Gemini => NoteGemini,
            AgentEngineKind.Shared => NoteShared,
            _ => NoteClaude,
        };
    }

    /// <summary>
    /// 全テンプレートをスタジオ内の既存メンバーと照合し、登録済み(Idまたは呼び名が重複)のものを
    /// 記録して選択状態を外す(BUG-18)。
    /// </summary>
    private void MarkRegisteredTemplates()
    {
        _registered.Clear();
        if (_studioAgentsProvider is null)
        {
            return;
        }

        var studioAgents = _studioAgentsProvider();
        foreach (var item in _categories.SelectMany(c => c.Templates))
        {
            var conflict = AgentNameUniqueness.FindConflict(item.Id, item.DisplayName, studioAgents);
            if (conflict is not null)
            {
                _registered[item] = conflict;
                item.IsSelected = false;
            }
        }
    }

    /// <summary>
    /// 全テンプレートについて、追加先以外のスコープに同Idのメンバーがいるかを調べて記録する。
    /// 案内の表示だけに使い、チェックや追加は妨げない。
    /// </summary>
    private void MarkOtherScopeTemplates()
    {
        _otherScopeLabels.Clear();
        if (_allStudioAgentsProvider is null)
        {
            return;
        }

        var allAgents = _allStudioAgentsProvider();
        foreach (var item in _categories.SelectMany(c => c.Templates))
        {
            var labels = OtherScopeMemberFinder.FindOtherScopeLabels(item.Id, _targetAgentDir, allAgents);
            if (labels.Count > 0)
            {
                _otherScopeLabels[item] = labels;
            }
        }
    }

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryComboBox.SelectedItem is AgentTemplateCategory category)
        {
            _currentCategory = category;
            CategoryDescriptionText.Text = category.Description;
            RenderTemplateItems();
            UpdateSelectedCount();
        }
    }

    /// <summary>
    /// 共有テーマ(Themes)に定義された配色ブラシを取得する。
    /// </summary>
    /// <param name="key">ブラシのリソースキー。</param>
    /// <returns>該当するブラシ。</returns>
    private Brush ThemeBrush(string key) => (Brush)FindResource(key);

    private void RenderTemplateItems()
    {
        TemplatesItemsPanel.Children.Clear();
        SharedStateNoteText.Visibility = Visibility.Collapsed;
        if (_currentCategory == null) return;

        foreach (var item in _currentCategory.Templates)
        {
            var rowBorder = new Border
            {
                Background = ThemeBrush("PanelBackgroundBrush"),
                BorderBrush = ThemeBrush("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("CornerRadiusMedium"),
                Padding = new Thickness(12, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: チェックボックス
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: アバター
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2: 名前・説明
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: モデル選択
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 4: ツール

            // 0. チェックボックス
            var isRegistered = _registered.TryGetValue(item, out var registeredConflict);
            var checkBox = new CheckBox
            {
                IsChecked = item.IsSelected && !isRegistered,
                IsEnabled = !isRegistered,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            if (isRegistered)
            {
                rowBorder.Opacity = 0.55;
                rowBorder.ToolTip = registeredConflict!.ToMessage();
            }
            // 追加先以外のスコープにも同Idのメンバーがいる場合は、控えめな案内を出す(登録済みの行は既存の表示を優先)
            IReadOnlyList<string>? otherLabels = null;
            var hasOtherScope = !isRegistered && _otherScopeLabels.TryGetValue(item, out otherLabels);
            if (hasOtherScope)
            {
                rowBorder.ToolTip = OtherScopeMemberFinder.SharedStateNote;
                SharedStateNoteText.Text = $"ℹ️ {OtherScopeMemberFinder.SharedStateNote}";
                SharedStateNoteText.Visibility = Visibility.Visible;
            }
            checkBox.Checked += (_, _) => { item.IsSelected = true; UpdateSelectedCount(); };
            checkBox.Unchecked += (_, _) => { item.IsSelected = false; UpdateSelectedCount(); };
            Grid.SetColumn(checkBox, 0);
            grid.Children.Add(checkBox);

            // 1. アバター
            var avatarBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString(item.AvatarColor)!,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            var initial = new TextBlock
            {
                Text = item.DisplayName.Contains('(') ? item.DisplayName.Split('(')[1].TrimEnd(')')[..1] : item.DisplayName[..1],
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            avatarBorder.Child = initial;
            Grid.SetColumn(avatarBorder, 1);
            grid.Children.Add(avatarBorder);

            // 2. 名前・性格・説明
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeBrush("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 8, 0),
            });
            if (isRegistered)
            {
                titlePanel.Children.Add(new TextBlock
                {
                    Text = $"⚠️登録済み ({registeredConflict!.Existing.ScopeLabel})",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#DC2626")!,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                });
            }
            if (hasOtherScope)
            {
                titlePanel.Children.Add(new TextBlock
                {
                    Text = OtherScopeMemberFinder.BuildBadgeText(otherLabels!),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeBrush("TextMutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                });
            }
            titlePanel.Children.Add(new Border
            {
                Background = ThemeBrush("PanelInsetBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = item.Personality,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeBrush("TextPrimaryBrush"),
                },
            });
            textPanel.Children.Add(titlePanel);

            textPanel.Children.Add(new TextBlock
            {
                Text = item.Description,
                FontSize = 11.5,
                Foreground = ThemeBrush("TextMutedBrush"),
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(textPanel, 2);
            grid.Children.Add(textPanel);

            // 3. モデル選択 ComboBox
            var modelPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
            modelPanel.Children.Add(new TextBlock
            {
                Text = "モデル:",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("TextMutedBrush"),
                Margin = new Thickness(0, 0, 0, 2),
            });
            var modelCombo = new ComboBox
            {
                ItemsSource = ModelTierMap.AllTiers.Select(t => ModelTierMap.Label(t)).ToArray(),
                SelectedItem = ModelTierMap.Label(item.SelectedTier),
                Width = ModelComboWidth,
                Height = 26,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                ToolTip = ModelTierMap.Tooltip(item.SelectedTier),
            };
            modelCombo.SelectionChanged += (_, _) =>
            {
                if (ModelTierMap.FromLabel(modelCombo.SelectedItem as string) is { } tier)
                {
                    item.SelectedTier = tier;
                    modelCombo.ToolTip = ModelTierMap.Tooltip(tier);
                }
            };
            modelPanel.Children.Add(modelCombo);
            Grid.SetColumn(modelPanel, 3);
            grid.Children.Add(modelPanel);

            // 4. ツール権限バッジ
            var toolPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            toolPanel.Children.Add(new TextBlock
            {
                Text = "権限:",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("TextMutedBrush"),
                Margin = new Thickness(0, 0, 0, 2),
            });
            toolPanel.Children.Add(new Border
            {
                Background = ThemeBrush("MenuItemHighlightBrush"),
                BorderBrush = ThemeBrush("PopupBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                Child = new TextBlock
                {
                    Text = item.Tools,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeBrush("AccentGreenBrush"),
                },
            });
            Grid.SetColumn(toolPanel, 4);
            grid.Children.Add(toolPanel);

            rowBorder.Child = grid;
            TemplatesItemsPanel.Children.Add(rowBorder);
        }
    }

    private void UpdateSelectedCount()
    {
        if (_currentCategory == null) return;
        var count = _currentCategory.Templates.Count(t => t.IsSelected && !_registered.ContainsKey(t));
        SelectedCountText.Text = $"{count} 名";
        ImportSelectedButton.Content = $"🚀 選択したメンバーをチームに追加 ({count}名)";
        ImportSelectedButton.IsEnabled = count > 0;
        ImportSelectedButton.Opacity = count > 0 ? 1.0 : 0.5;
    }

    private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_currentCategory == null) return;
        foreach (var item in _currentCategory.Templates) item.IsSelected = !_registered.ContainsKey(item);
        RenderTemplateItems();
        UpdateSelectedCount();
    }

    private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_currentCategory == null) return;
        foreach (var item in _currentCategory.Templates) item.IsSelected = false;
        RenderTemplateItems();
        UpdateSelectedCount();
    }

    private void ApplyRecommendedModels_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategory == null) return;
        foreach (var item in _currentCategory.Templates) item.SelectedTier = ModelTierMap.FromClaudeModel(item.RecommendedModel);
        RenderTemplateItems();
    }

    /// <summary>「全員 高速・低コスト」: 現在のカテゴリの全員を高速・低コスト(標準バランス)にする。</summary>
    private void ApplyAllStandard_Click(object sender, RoutedEventArgs e) => ApplyAllTier(ModelTier.Standard);

    /// <summary>「全員 超軽量・最速」: 現在のカテゴリの全員を超軽量・最速にする。</summary>
    private void ApplyAllUltraLight_Click(object sender, RoutedEventArgs e) => ApplyAllTier(ModelTier.UltraLight);

    /// <summary>現在のカテゴリの全テンプレートを指定の段階にして再描画する。</summary>
    /// <param name="tier">設定する段階。</param>
    private void ApplyAllTier(ModelTier tier)
    {
        if (_currentCategory == null) return;
        foreach (var item in _currentCategory.Templates) item.SelectedTier = tier;
        RenderTemplateItems();
    }

    private void ImportSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategory == null) return;
        // 登録済みのテンプレートは、選択状態に関わらず取り込まない(BUG-18)。
        var selectedItems = _currentCategory.Templates.Where(t => t.IsSelected && !_registered.ContainsKey(t)).ToList();
        if (selectedItems.Count == 0) return;

        try
        {
            var engine = SelectedEngine;
            var created = 0;
            var skipped = 0;
            var bundledAvatarDir = Path.Combine([AppContext.BaseDirectory, .. BundledAvatarRelativePath]);

            foreach (var item in selectedItems)
            {
                var plan = AgentDeployWriter.Plan(_targetAgentDir, _targetGeminiSkillsDir, item.Id, engine);
                if (plan.HasExisting)
                {
                    var result = MessageBox.Show(
                        this,
                        $"エージェント「{item.DisplayName} ({item.Id})」は既に存在します。{Environment.NewLine}" +
                        $"{string.Join(Environment.NewLine, plan.ExistingPaths)}{Environment.NewLine}上書きしますか？",
                        "上書き確認",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel) return;
                    if (result == MessageBoxResult.No)
                    {
                        skipped++;
                        continue;
                    }
                }

                // 編集画面と同じ共通処理で書き出す(選択段階に対応するClaude/Geminiモデルをそれぞれ渡す)
                AgentDeployWriter.Write(
                    plan, _targetAgentDir, _targetGeminiSkillsDir, item.Id, item.Description, item.Tools,
                    ModelTierMap.ClaudeModel(item.SelectedTier), ModelTierMap.GeminiModel(item.SelectedTier),
                    color: null, item.SystemPrompt, item.DisplayName, avatarFileName: null,
                    removeUndeployedSide: false);

                // アバター画像(Assets/Avatars)が存在すれば配置先へ自動コピーしてトレカカードを完成させる
                AgentDeployWriter.CopyBundledAvatarIfExists(plan, _targetAgentDir, item.Id, bundledAvatarDir);

                created++;
            }

            ImportedCount = created;
            MessageBox.Show(
                this,
                $"チーム「{_targetScopeName}」に {created} 名のエージェントを追加しました！\n（スキップ: {skipped} 件）",
                "取り込み完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"エージェントの作成中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
