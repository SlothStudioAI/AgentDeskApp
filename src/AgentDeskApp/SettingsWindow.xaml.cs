using System.Windows;

namespace AgentDeskApp;

/// <summary>
/// スタジオの環境設定(監視ポーリング周期、CLIパス等)を行う設定ダイアログ。
/// 設定値は <see cref="AppSettingsLoader"/> 経由で %APPDATA%\AgentDeskApp\settings.json に永続化される。
/// </summary>
public partial class SettingsWindow : Window
{
    private AppSettings _currentSettings;

    /// <summary>設定が保存されたらtrue。</summary>
    public bool Saved { get; private set; }

    /// <summary>保存後の最新設定。</summary>
    public AppSettings UpdatedSettings => _currentSettings;

    public SettingsWindow()
    {
        InitializeComponent();

        _currentSettings = AppSettingsLoader.Load();

        SessionIntervalTextBox.Text = _currentSettings.SessionRefreshIntervalSeconds.ToString();
        LogIntervalTextBox.Text = _currentSettings.LogPollIntervalSeconds.ToString();

        // 作業中の揺れ・完了通知の各演出のオン/オフ
        RunningSwayCheckBox.IsChecked = _currentSettings.RunningSwayEnabled;
        CompletionSoundCheckBox.IsChecked = _currentSettings.CompletionSoundEnabled;
        AvatarBounceCheckBox.IsChecked = _currentSettings.AvatarBounceEnabled;
        TaskbarFlashCheckBox.IsChecked = _currentSettings.TaskbarFlashEnabled;

        // 空欄のままだと「本当に動くのか」不安になるため、未設定時は実際に使われる既定値を表示する (U-11)
        ClaudeCliTextBox.Text = _currentSettings.EffectiveClaudeCliPath;
        GeminiCliTextBox.Text = _currentSettings.EffectiveGeminiCliPath;

        // 既存ユーザーが非標準インストール場所などに手動でCLIパスを設定済みの場合、
        // それを無警告でnull(=自動探索)に上書きしてしまわないよう、保存済みの値が
        // 自動探索結果と異なるなら「手動で指定する」をONにして復元する(シラベ指摘対応)。
        InitializeManualOverrideState(
            ClaudeCliManualOverrideCheckBox,
            ClaudeCliTextBox,
            RefreshClaudeCliButton,
            _currentSettings.ClaudeCliPath,
            CliPathResolver.Resolve(CliConstants.DefaultClaudeCliName, CliConstants.ClaudeCliFallbackPaths));
        InitializeManualOverrideState(
            GeminiCliManualOverrideCheckBox,
            GeminiCliTextBox,
            RefreshGeminiCliButton,
            _currentSettings.GeminiCliPath,
            CliPathResolver.Resolve(CliConstants.DefaultGeminiCliName, CliConstants.GeminiCliFallbackPaths));

        FilePathNoticeText.Text = $"設定ファイル: {AppSettingsLoader.SettingsFilePath}";

        _ = RefreshCliStatusAsync(ClaudeCliTextBox.Text, ClaudeCliStatusText);
        _ = RefreshCliStatusAsync(GeminiCliTextBox.Text, GeminiCliStatusText);
    }

    /// <summary>
    /// CLIパス欄の「手動で指定する」チェックボックスの初期状態を決定する。
    /// 保存済みの手動パスが存在し、かつ自動探索結果と異なる場合のみONにすることで、
    /// 既存の非標準環境向け手動設定を保存時に無警告消去しないようにする(シラベ指摘対応)。
    /// </summary>
    /// <param name="checkBox">対象の「手動で指定する」チェックボックス。</param>
    /// <param name="textBox">対象のCLIパス入力欄。</param>
    /// <param name="refreshButton">対象の「🔄 再探索」ボタン。</param>
    /// <param name="savedCliPath">settings.jsonに保存されている生のCLIパス値(未設定ならnull)。</param>
    /// <param name="autoResolvedPath">自動探索した場合に得られる結果。</param>
    private static void InitializeManualOverrideState(
        System.Windows.Controls.CheckBox checkBox,
        System.Windows.Controls.TextBox textBox,
        System.Windows.Controls.Button refreshButton,
        string? savedCliPath,
        string autoResolvedPath)
    {
        var isManual = !string.IsNullOrWhiteSpace(savedCliPath) && !string.Equals(savedCliPath, autoResolvedPath, StringComparison.OrdinalIgnoreCase);
        checkBox.IsChecked = isManual;
        ApplyManualOverrideVisualState(checkBox, textBox, refreshButton);
    }

    /// <summary>
    /// 「手動で指定する」チェックボックスの状態に、テキストボックスの編集可否・背景色・
    /// 「🔄 再探索」ボタンの有効/無効を連動させる。
    /// </summary>
    /// <param name="checkBox">状態の基準となるチェックボックス。</param>
    /// <param name="textBox">連動させるCLIパス入力欄。</param>
    /// <param name="refreshButton">連動させる「🔄 再探索」ボタン。</param>
    private static void ApplyManualOverrideVisualState(
        System.Windows.Controls.CheckBox checkBox,
        System.Windows.Controls.TextBox textBox,
        System.Windows.Controls.Button refreshButton)
    {
        var isManual = checkBox.IsChecked == true;
        textBox.IsReadOnly = !isManual;
        // 手動指定中は入力欄(白)、自動検出中は読み取り専用の淡い色(共有テーマのブラシ)にする
        textBox.Background = (System.Windows.Media.Brush)textBox.FindResource(isManual ? "InputBackgroundBrush" : "PanelInsetBrush");
        refreshButton.IsEnabled = !isManual;
    }

    /// <summary>
    /// Claude CLIの「✏️ 手動で指定する」チェック切り替え時の処理。テキストボックスの
    /// 編集可否等をチェック状態に連動させる(シラベ指摘対応)。
    /// </summary>
    /// <param name="sender">イベント送信元のチェックボックス。</param>
    /// <param name="e">ルーティングイベント引数。</param>
    private void ClaudeCliManualOverrideCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyManualOverrideVisualState(ClaudeCliManualOverrideCheckBox, ClaudeCliTextBox, RefreshClaudeCliButton);
    }

    /// <summary>
    /// Gemini CLIの「✏️ 手動で指定する」チェック切り替え時の処理。テキストボックスの
    /// 編集可否等をチェック状態に連動させる(シラベ指摘対応)。
    /// </summary>
    /// <param name="sender">イベント送信元のチェックボックス。</param>
    /// <param name="e">ルーティングイベント引数。</param>
    private void GeminiCliManualOverrideCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyManualOverrideVisualState(GeminiCliManualOverrideCheckBox, GeminiCliTextBox, RefreshGeminiCliButton);
    }

    /// <summary>
    /// 「🔄 再探索」(Claude)ボタン押下時の処理。<see cref="CliPathResolver"/>による自動探索を
    /// 再実行し、表示欄と検出ステータスを更新する (U-26)。
    /// </summary>
    /// <param name="sender">イベント送信元のボタン。</param>
    /// <param name="e">ルーティングイベント引数。</param>
    private void RefreshClaudeCliButton_Click(object sender, RoutedEventArgs e)
    {
        ClaudeCliTextBox.Text = CliPathResolver.Resolve(CliConstants.DefaultClaudeCliName, CliConstants.ClaudeCliFallbackPaths);
        _ = RefreshCliStatusAsync(ClaudeCliTextBox.Text, ClaudeCliStatusText);
    }

    /// <summary>
    /// 「🔄 再探索」(Gemini)ボタン押下時の処理。<see cref="CliPathResolver"/>による自動探索を
    /// 再実行し、表示欄と検出ステータスを更新する (U-26)。
    /// </summary>
    /// <param name="sender">イベント送信元のボタン。</param>
    /// <param name="e">ルーティングイベント引数。</param>
    private void RefreshGeminiCliButton_Click(object sender, RoutedEventArgs e)
    {
        GeminiCliTextBox.Text = CliPathResolver.Resolve(CliConstants.DefaultGeminiCliName, CliConstants.GeminiCliFallbackPaths);
        _ = RefreshCliStatusAsync(GeminiCliTextBox.Text, GeminiCliStatusText);
    }

    /// <summary>
    /// Claude CLIパス欄のフォーカスが外れた際の処理(改善7)。手動入力直後でもボタン操作を挟まずに
    /// 検出ステータスへ即時反映できるよう、フォーカスアウトのタイミングで存在確認を再実行する。
    /// </summary>
    /// <param name="sender">イベント送信元のテキストボックス。</param>
    /// <param name="e">ルーティングイベント引数。</param>
    private void ClaudeCliTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = RefreshCliStatusAsync(ClaudeCliTextBox.Text, ClaudeCliStatusText);
    }

    /// <summary>
    /// Gemini CLIパス欄のフォーカスが外れた際の処理(改善7)。手動入力直後でもボタン操作を挟まずに
    /// 検出ステータスへ即時反映できるよう、フォーカスアウトのタイミングで存在確認を再実行する。
    /// </summary>
    /// <param name="sender">イベント送信元のテキストボックス。</param>
    /// <param name="e">ルーティングイベント引数。</param>
    private void GeminiCliTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = RefreshCliStatusAsync(GeminiCliTextBox.Text, GeminiCliStatusText);
    }

    /// <summary>
    /// 指定したCLIコマンド/パスが実際に実行可能かを検出し、ステータス表示を更新する (U-11)。
    /// </summary>
    private static async Task RefreshCliStatusAsync(string cliPath, System.Windows.Controls.TextBlock statusText)
    {
        cliPath = cliPath.Trim();
        if (cliPath.Length == 0)
        {
            statusText.Text = "⚠️ コマンド/パスを入力してください";
            statusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        statusText.Text = "🔍 確認中...";
        statusText.Foreground = System.Windows.Media.Brushes.Gray;

        var version = await CliAvailability.DetectVersionAsync(cliPath);
        if (version != null)
        {
            statusText.Text = $"✅ 検出済み ({version})";
            statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        else
        {
            statusText.Text = "⚠️ 見つかりません。PATHまたはフルパスを確認してください";
            statusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SessionIntervalTextBox.Text.Trim(), out var sessionInterval) || sessionInterval < 1)
        {
            MessageBox.Show(this, "セッション更新周期には1以上の正の整数を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(LogIntervalTextBox.Text.Trim(), out var logInterval) || logInterval < 1)
        {
            MessageBox.Show(this, "ログ追跡周期には1以上の正の整数を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 「手動で指定する」がOFFの場合は常に自動探索(CliPathResolver)に委ねるためnullを保存する(U-26)。
        // ONの場合は非標準インストール場所などユーザーが明示指定したパスを尊重し、そのまま保存する。
        // これにより、既存の手動設定ユーザーが他項目だけ変更して保存しても設定がサイレント消去されない
        // (シラベ指摘対応)。
        var claudeCliPathToSave = ClaudeCliManualOverrideCheckBox.IsChecked == true
            ? NullIfWhitespace(ClaudeCliTextBox.Text)
            : null;
        var geminiCliPathToSave = GeminiCliManualOverrideCheckBox.IsChecked == true
            ? NullIfWhitespace(GeminiCliTextBox.Text)
            : null;

        _currentSettings = _currentSettings with
        {
            SessionRefreshIntervalSeconds = sessionInterval,
            LogPollIntervalSeconds = logInterval,
            ClaudeCliPath = claudeCliPathToSave,
            GeminiCliPath = geminiCliPathToSave,
            RunningSwayEnabled = RunningSwayCheckBox.IsChecked == true,
            CompletionSoundEnabled = CompletionSoundCheckBox.IsChecked == true,
            AvatarBounceEnabled = AvatarBounceCheckBox.IsChecked == true,
            TaskbarFlashEnabled = TaskbarFlashCheckBox.IsChecked == true,
        };

        AppSettingsLoader.Save(_currentSettings);

        Saved = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 文字列がnullまたは空白のみの場合にnullを返し、それ以外はトリムして返す。
    /// 手動指定CLIパスの保存時に空欄を「未設定(null)」として扱うために用いる。
    /// </summary>
    /// <param name="value">判定対象の文字列。</param>
    private static string? NullIfWhitespace(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
