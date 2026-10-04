using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgentDeskApp;

/// <summary>
/// メンバーカードの「📋 Task」ボタンから開く、タスク実行履歴・ログビューアウィンドウ。
/// </summary>
public partial class AgentTaskHistoryWindow : Window
{
    private readonly AgentDefinition _agent;

    public AgentTaskHistoryWindow(AgentDefinition agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        InitializeComponent();

        SetupAgentInfo();
        LoadTaskHistory();
    }

    private void SetupAgentInfo()
    {
        AgentNameText.Text = _agent.EffectiveDisplayName;
        AgentRoleText.Text = string.IsNullOrWhiteSpace(_agent.Description) ? "役割未設定" : _agent.Description;

        // エンジンバッジ
        EngineBadgeText.Text = _agent.Engine switch
        {
            AgentEngineKind.Shared => "🔄 Shared",
            AgentEngineKind.Gemini => "🌟 Gemini",
            _ => "🦥 Claude",
        };

        // アバター表示
        if (!string.IsNullOrEmpty(_agent.AvatarPath) && File.Exists(_agent.AvatarPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_agent.AvatarPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                AvatarBorder.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                AvatarBorder.Child = null;
                return;
            }
            catch
            {
                // フォールバック
            }
        }

        var initial = string.IsNullOrEmpty(_agent.EffectiveDisplayName) ? "?" : _agent.EffectiveDisplayName[..1].ToUpperInvariant();
        AvatarBorder.Background = TryParseColor(_agent.Color) ?? new SolidColorBrush(Color.FromRgb(99, 102, 241));
        AvatarBorder.Child = new TextBlock
        {
            Text = initial,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        };
    }

    private static SolidColorBrush? TryParseColor(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return null;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return null;
        }
    }

    private void LoadTaskHistory()
    {
        var records = AgentTaskHistoryReader.GetTaskHistory(_agent.Name);
        TotalCountText.Text = $"{records.Count} 件のタスク履歴";

        if (records.Count == 0)
        {
            EmptyNoticeText.Visibility = Visibility.Visible;
            TaskListBox.ItemsSource = null;
            ClearDetail();
        }
        else
        {
            EmptyNoticeText.Visibility = Visibility.Collapsed;
            TaskListBox.ItemsSource = records;
            TaskListBox.SelectedIndex = 0;
        }
    }

    private bool _isInstructionExpanded;
    private bool _isResultExpanded;

    private void ClearDetail()
    {
        DetailUserRequestText.Text = "（タスクが選択されていません）";
        DetailInstructionText.Text = "（タスクが選択されていません）";
        DetailEngineText.Text = "-";
        DetailSessionIdText.Text = "-";
        DetailTimestampText.Text = "-";
        DetailResultText.Text = "-";
        InstructionLengthBadge.Text = "";
        ResultLengthBadge.Text = "";
        ToggleInstructionButton.Visibility = Visibility.Collapsed;
        ToggleResultButton.Visibility = Visibility.Collapsed;
    }

    private void TaskListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TaskListBox.SelectedItem is AgentTaskRecord record)
        {
            DetailUserRequestText.Text = !string.IsNullOrEmpty(record.UserRequest)
                ? record.UserRequest
                : "（直接プロンプトまたはCLI指示から実行）";

            // リーダーの指示
            DetailInstructionText.Text = record.Instruction;
            var instLength = record.Instruction.Length;
            InstructionLengthBadge.Text = $"({instLength:N0}文字)";
            _isInstructionExpanded = false;
            DetailInstructionText.MaxHeight = 120;
            if (instLength > 200 || record.Instruction.Count(c => c == '\n') >= 3)
            {
                ToggleInstructionButton.Visibility = Visibility.Visible;
                ToggleInstructionButton.Content = "▼ 全文を展開する";
            }
            else
            {
                ToggleInstructionButton.Visibility = Visibility.Collapsed;
            }

            // メタデータ
            DetailEngineText.Text = record.Engine;
            DetailSessionIdText.Text = record.SessionId;
            DetailTimestampText.Text = record.Timestamp.ToString("yyyy/MM/dd HH:mm:ss");

            // 実行結果・AI回答
            var result = record.ResultSummary ?? "詳細サマリーなし";
            DetailResultText.Text = result;
            var resLength = result.Length;
            ResultLengthBadge.Text = $"({resLength:N0}文字)";
            _isResultExpanded = false;
            DetailResultText.MaxHeight = 180;
            if (resLength > 250 || result.Count(c => c == '\n') >= 5)
            {
                ToggleResultButton.Visibility = Visibility.Visible;
                ToggleResultButton.Content = "▼ 全文を展開する";
            }
            else
            {
                ToggleResultButton.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            ClearDetail();
        }
    }

    private void ToggleInstructionButton_Click(object sender, RoutedEventArgs e)
    {
        _isInstructionExpanded = !_isInstructionExpanded;
        if (_isInstructionExpanded)
        {
            DetailInstructionText.MaxHeight = double.PositiveInfinity;
            ToggleInstructionButton.Content = "▲ 折りたたむ";
        }
        else
        {
            DetailInstructionText.MaxHeight = 120;
            ToggleInstructionButton.Content = "▼ 全文を展開する";
        }
    }

    private void ToggleResultButton_Click(object sender, RoutedEventArgs e)
    {
        _isResultExpanded = !_isResultExpanded;
        if (_isResultExpanded)
        {
            DetailResultText.MaxHeight = double.PositiveInfinity;
            ToggleResultButton.Content = "▲ 折りたたむ";
        }
        else
        {
            DetailResultText.MaxHeight = 180;
            ToggleResultButton.Content = "▼ 全文を展開する";
        }
    }

    private void CopyUserRequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailUserRequestText.Text) && DetailUserRequestText.Text != "（タスクが選択されていません）")
        {
            Clipboard.SetText(DetailUserRequestText.Text);
            CopyUserRequestButton.Content = "✅ コピー済";
        }
    }

    private void CopyInstructionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailInstructionText.Text) && DetailInstructionText.Text != "（タスクが選択されていません）")
        {
            Clipboard.SetText(DetailInstructionText.Text);
            CopyInstructionButton.Content = "✅ コピー済";
        }
    }

    private void CopyResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailResultText.Text) && DetailResultText.Text != "-")
        {
            Clipboard.SetText(DetailResultText.Text);
            CopyResultButton.Content = "✅ コピー済";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadTaskHistory();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
