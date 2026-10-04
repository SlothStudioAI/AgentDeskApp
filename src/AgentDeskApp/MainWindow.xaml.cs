﻿﻿using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace AgentDeskApp;

/// <summary>
/// A案モックアップを忠実に再現した「Sloth Studio」メインウィンドウ。
/// 左サイドバー、上部スタジオヘッダー、Leader/Youワイドバナー、縦長スタジオメンバーカード（カラー天面＋アバター＋状態＋メトリクス＋アクション）を提供。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// メンバーカードの幅。2026-10-03に旧236×364を約3/4へ縮小（縦横比維持）。
    /// 1枚あたり幅177＋右余白60＝237px。フルHD最大化(メイン幅1672-左右余白64-スクロールバー17=1591〜1608)で6人並び(6×237=1422)、7人目は入らない(7×237=1659超)。
    /// カード間隔は <see cref="CardGapHorizontal"/> と <see cref="CardGapVertical"/> で変更できる。
    /// </summary>
    private const double CardWidth = 177;

    /// <summary>メンバーカードの高さ。幅177に合わせ、旧236×364の縦横比を維持して273にした（2026-10-03）。</summary>
    private const double CardHeight = 273;

    // ===== 角丸の大きさ(柔らかい森の印象。丸みの調整は MainWindow.xaml 先頭の CornerRadius* リソースと、ここの定数で行う) =====

    /// <summary>メンバーカード・「+ Add New Member」破線カードの角丸半径(px)。カード幅177に対し大きめの柔らかい丸み。</summary>
    private const double CardCornerRadius = 20;

    /// <summary>リーダー(相棒)カードの角丸半径(px)。カードが大きい(260×400)ので少し大きめにして印象をそろえる。</summary>
    private const double LeaderCardCornerRadius = 24;

    /// <summary>カードの枠線の太さ(px)。中身の切り抜きの角丸は「外側の角丸 − この値」になる。</summary>
    private const double CardBorderThickness = 1;

    /// <summary>カード上に重ねる小さなバッジ・ピル(AI名・メニュー・相棒ラベル等)の角丸半径(px)。十分に大きくして両端を半円にする。</summary>
    private const double CardBadgeCornerRadius = 999; // ⋮ ボタン用(丸いまま)
    /// <summary>リーダーカードのバッジ(相棒・AI名・会話件数)の角丸(角丸化前の値に戻したもの)。</summary>
    private const double LeaderBadgeCornerRadius = 9;
    /// <summary>メンバーカードのAI名バッジの角丸(角丸化前の値に戻したもの)。</summary>
    private const double MemberEngineBadgeCornerRadius = 10;

    /// <summary>
    /// ウィンドウ外形の角丸の設定値(DWMWA_WINDOW_CORNER_PREFERENCE)。0=OS既定 / 1=丸めない / 2=丸める(標準の丸み) / 3=小さく丸める。
    /// Windows 11 のみ有効(Windows 10 以前では無視され、四角のまま)。最大化・スナップ時はOSが自動で角を直角に戻す。
    /// </summary>
    private const int WindowCornerPreference = 2;

    /// <summary>DwmSetWindowAttribute に渡す「ウィンドウの角の丸み設定」の属性番号(DWMWA_WINDOW_CORNER_PREFERENCE)。</summary>
    private const int DwmWindowCornerPreferenceAttribute = 33;

    /// <summary>ウィンドウの属性をDWM(デスクトップウィンドウマネージャー)に設定するWin32 API。</summary>
    /// <param name="hwnd">対象ウィンドウのハンドル。</param>
    /// <param name="attribute">属性番号。</param>
    /// <param name="value">設定値。</param>
    /// <param name="valueSize">設定値のバイト数。</param>
    /// <returns>成功なら0。</returns>
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    /// <summary>メンバーカード同士の水平方向の間隔(px。各カードの右Margin)。</summary>
    private const double CardGapHorizontal = 60;

    /// <summary>メンバーカード同士の垂直方向の間隔(px。各カードの下Margin)。</summary>
    private const double CardGapVertical = 60;

    /// <summary>メンバーカード下部の黒エリアの左右余白(px)。カード縮小(177×273)に合わせて詰めた。</summary>
    private const double MemberCardTextAreaPaddingH = 10;
    /// <summary>メンバーカード左上/右上のバッジの文字サイズ。</summary>
    private const double MemberCardBadgeFontSize = 9.5;
    /// <summary>Shared(両エンジン配備)メンバーのバッジの文字サイズ。「🦥 Claude  🌟 Gemini」を右上の⋮ボタンと重ならず1行で収めるため、基準より少し小さくする。</summary>
    private const double MemberCardSharedBadgeFontSize = 9;
    /// <summary>Shared(両エンジン配備)メンバーのバッジ文言。</summary>
    private const string MemberSharedBadgeText = "🦥 Claude  🌟 Gemini";
    /// <summary>メンバーカード左上/右上のバッジとカード縁の間隔(px)。</summary>
    private const double MemberCardBadgeMargin = 7;
    /// <summary>メンバーカードの名前の文字サイズ。</summary>
    private const double MemberCardNameFontSize = 13.5;
    /// <summary>メンバーカードのロール行の文字サイズ。</summary>
    private const double MemberCardRoleFontSize = 11;
    /// <summary>Dashboard上部のカードに小さく出す所属チーム名の文字サイズ。</summary>
    private const double MemberCardTeamLabelFontSize = 10;
    /// <summary>Dashboard上部のカードの所属チーム名の前に付ける目印。</summary>
    private const string MemberCardTeamLabelPrefix = "👥 ";

    /// <summary>カード内のアバター要素(画像またはイニシャル文字)を見つけるためのTag。「ぷるん」「ゆらゆら」演出の対象。</summary>
    private const string AvatarVisualTag = "AvatarVisual";

    /// <summary>カード内の、完了後の光る枠用の要素(中身の上に重ねる)を見つけるためのTag。</summary>
    private const string GlowFrameTag = "GlowFrame";

    /// <summary>「コピーしました」等の一時表示を消すためのタイマー(表示中に再表示されたら延長する)。</summary>
    private DispatcherTimer? _transientMessageTimer;

    private static readonly SolidColorBrush CardBorderDefault = new((Color)ColorConverter.ConvertFromString("#E2E8F0")!);

    private static readonly string GlobalAgentsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "agents");

    private static readonly string GlobalGeminiSkillsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", "skills");

    private static readonly string GlobalClaudeMdPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "CLAUDE.md");

    private sealed record StudioNotification(string Title, string Message, DateTime Timestamp, bool IsRead = false);

    /// <summary>
    /// TEAM欄のリーダー(相棒)カードの幅。メンバーカード(236×364)と同じ作りの縦長カードで、
    /// ユーザー要望「下のメンバーカードより大きいくらい」に合わせて一回り大きく(約1.1倍)した(2026-10-03)。
    /// 縦横比はメンバーカードとほぼ同じ(400/260≒1.54、364/236≒1.54)。画面の作りに関わる値なので設定ファイル化はせず定数とする
    /// (メンバーカードの <see cref="CardWidth"/> と同じ扱い)。
    /// </summary>
    private const double LeaderCardWidth = 260;

    /// <summary>TEAM欄のリーダー(相棒)カードの高さ。幅と同じ理由で、メンバーカードより一回り大きくした。</summary>
    private const double LeaderCardHeight = 400;

    /// <summary>リーダーカード同士・左のチーム情報との間隔(カードの左側の余白)。</summary>
    private const double LeaderCardSpacing = 16;

    /// <summary>リーダーカードが折り返して2段になったときの段の間隔(カードの下側の余白)。</summary>
    private const double LeaderCardRowSpacing = 12;
    /// <summary>リーダーカード置き場の現在の配置(0=中央寄せ / 1=右寄せ(XAML既定) / 2=下の段)。</summary>
    private int _leaderLayoutMode = 1;
    /// <summary>リーダーカード下部の文字エリアの最小高さ(画像の上に重ねる黒っぽい帯。キャラ名1行(20pt)がぎりぎり収まる高さ)。</summary>
    private const double LeaderCardTextAreaMinHeight = 46;
    /// <summary>メンバーカードのマウスオーバー説明(ToolTip)の最大幅(px)。これを超える文は折り返す。</summary>
    private const double MemberCardToolTipMaxWidth = 320;
    /// <summary>メンバーカードのポップアップをカーソルから右へずらす量(px)。カーソルの矢印に被らず、ほぼ隣に見える最小限の余白。</summary>
    private const double MemberCardToolTipOffsetX = 8;
    /// <summary>メンバーカードのポップアップをカーソルから下へずらす量(px)。矢印の先端・下部に重ならない最小限の余白。</summary>
    private const double MemberCardToolTipOffsetY = 12;
    /// <summary>ポップアップ内の警告文(未接続・同名)の色(白っぽい背景で読める赤)。</summary>
    private const string MemberCardToolTipWarningColor = "#DC2626";
    /// <summary>リーダーカードの画像左上に重ねる小さな角丸背景(AI名・会話件数)の黒の不透明度(0〜255)。</summary>
    private const byte LeaderCardOverlayBackgroundAlpha = 150;
    /// <summary>リーダーカード上部のバッジ(AI名・会話件数・相棒)の上マージン(px)。3つで高さをそろえる。</summary>
    private const double LeaderCardTopBadgeMarginTop = 10;
    /// <summary>リーダーカード下部の文字エリアの背景色(木のイメージの茶色 #5B3A24。サイドバーの色とは連動させない)。</summary>
    private static readonly Color LeaderCardTextAreaBackgroundRgb = Color.FromRgb(0x5B, 0x3A, 0x24);
    /// <summary>リーダーカード下部の文字エリア背景の不透明度(0〜255)。画像が透ける程度(リーダー・メンバー共通)。</summary>
    private const byte LeaderCardTextAreaBackgroundAlpha = 170;
    /// <summary>リーダーカード下部の補助文字(ロール・モデル行など)の色。暗い背景で読める明るいグレー。</summary>
    private const string LeaderCardSubTextColor = "#E2E8F0";

    /// <summary>
    /// リーダーカード内の可変部分(キャラ名・会話件数)への参照をまとめたもの。
    /// ポーリング時にカードを作り直さず、これらのプロパティだけを更新することでチカチカを防ぐ。
    /// 状態バッジ・揺れ・光はメンバーカードと同じTagで探して更新する。
    /// </summary>
    private sealed class LeaderCardRefs
    {
        public required Border Card { get; init; }
        public required AgentEngineKind Engine { get; init; }
        public required TextBlock NameText { get; init; }
        public required TextBlock SessionCountText { get; init; }
    }

    private enum ScopeKind { Department, Group, Team }

    private sealed record ScopeContext(
        ScopeKind Kind,
        string DisplayName,
        string? FolderPath,
        string? ParentGroupPath,
        string RulePath
    );

    private sealed class GroupItem
    {
        public required string RootPath { get; init; }
        public required string DisplayName { get; init; }
        public List<TeamItem> Teams { get; } = [];
    }

    private sealed class TeamItem
    {
        public required string RootPath { get; init; }
        public required string DisplayName { get; init; }
        public required string ParentGroupPath { get; init; }

        /// <summary>
        /// チームフォルダ自体が独立したGitリポジトリかどうか(U-5)。
        /// 独立リポジトリの場合、VSCode等で直接開くと親ワークスペースのCLAUDE.md/GEMINI.mdが
        /// CLIから継承されず無視されてしまう(design.md「Git境界によるルールの断絶問題」)。
        /// 通常のリポジトリは`.git`がディレクトリだが、git worktreeやsubmoduleの場合は
        /// `.git`が親リポジトリへのパスを記載したテキストファイルになるため、
        /// File.Existsによる判定も合わせて行う(U-5+)。
        /// </summary>
        public bool IsIndependentGitRepo
        {
            get
            {
                var gitPath = Path.Combine(RootPath, ".git");
                return Directory.Exists(gitPath) || File.Exists(gitPath);
            }
        }
    }

    /// <summary>
    /// メンバーカードを表示する1つの区画(U-21)。グループ選択時は「グループ直下」＋チームごとに
    /// セクションを分け、それぞれ見出し付きでまとめて表示する。通常(チーム/全社)選択時は
    /// 見出し無しの単一セクションとして扱う。
    /// </summary>
    private sealed class MemberSection
    {
        public required string SectionId { get; init; }
        public string? Header { get; init; }
        public required string ClaudeDir { get; init; }
        public required string GeminiDir { get; init; }
        public required AgentScope ScopeType { get; init; }
        public required string OwnerLabel { get; init; }
        public required bool ShowAddMemberCard { get; init; }
    }

    /// <summary>セクション1つ分の描画済み要素(見出し込みの外枠と、カードを並べるWrapPanel)への参照。</summary>
    private sealed class SectionContainer
    {
        public required StackPanel Root { get; init; }
        public TextBlock? HeaderText { get; init; }
        public required WrapPanel CardsPanel { get; init; }
    }

    private readonly List<GroupItem> _groups = [];

    /// <summary>Id/呼び名が他のメンバーと重複している定義ファイルのパス(BUG-18の警告バッジ表示用)。</summary>
    private HashSet<string> _duplicateFilePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>直近に通知した重複内容の署名。変化した時だけ通知ベルに記録するための比較用。</summary>
    private string _lastDuplicateSignature = string.Empty;
    private ScopeContext _currentScope;
    private readonly MemberActivityMonitor _activityMonitor = new();
    private readonly List<Button> _navButtons = [];
    private readonly List<StudioNotification> _notifications = [];
    /// <summary>AI(エンジン)ごとのリーダーカード。1つのAIにつき1枚。</summary>
    private readonly Dictionary<AgentEngineKind, LeaderCardRefs> _leaderCards = [];

    /// <summary>AIごとの、直近にカードへ反映した内容(カードが画面に載り直したときの表示復元に使う)。</summary>
    private readonly Dictionary<AgentEngineKind, LeaderCardSummary> _leaderSummaries = [];

    /// <summary>
    /// リーダーカードの完了(作業中→完了)検知と「完了未確認」の管理。キーはAIの名前。
    /// 表示中の範囲(チーム/ワークスペース/全社)が変わると集める会話も変わるため、範囲が変わったら作り直す。
    /// </summary>
    private MemberCardAttentionTracker _leaderAttention = new();

    /// <summary>リーダーの完了検知を行った表示範囲の識別子(範囲の切り替えを検知するため)。</summary>
    private string? _leaderObservedScopeKey;
    private readonly Dictionary<string, SectionContainer> _memberSections = [];
    private readonly Dictionary<string, Dictionary<string, FrameworkElement>> _memberCardsBySection = [];

    /// <summary>Dashboard上部「稼働中・完了のメンバー」に出しているカード(キーはメンバーのId)。</summary>
    private readonly Dictionary<string, (Border Card, string TeamLabel)> _dashboardCards = new(StringComparer.Ordinal);

    /// <summary>
    /// Dashboardの上部エリアの候補にする、全チーム横断のメンバー一覧のキャッシュ。
    /// 5秒ごとの周期でディスクを読み直さないよう、画面の切り替え時と30秒周期の更新時にだけ読み直す。
    /// なお状態を検知できるのは候補メンバーが一度画面で読み込まれたもの(_knownMemberCandidates)だけという既存仕様に依存する。
    /// </summary>
    private IReadOnlyList<StudioAgentEntry> _dashboardEntries = [];

    /// <summary>F-2: サイドバーで折りたたみ中のワークスペース(グループ)ルートパスの集合。未登場=展開中。</summary>
    private readonly HashSet<string> _collapsedGroups = [];
    private bool _isClaudeCliAvailable = true;
    private bool _isGeminiCliAvailable = true;

    /// <summary>Q-1: sessionRefreshTimerの二重実行防止フラグ(0=非実行中, 1=実行中)。Interlockedで排他制御する。</summary>
    private int _isSessionRefreshing;
    /// <summary>完了(Running→Done)の検知と、クリックで確認されるまでカードを光らせる「完了未確認」の管理(メモリ上のみ)。</summary>
    private readonly MemberCardAttentionTracker _cardAttention = new();

    /// <summary>
    /// カードの演出(作業中の揺れ・完了後の光)に使う設定値。状態監視の周期ごとにファイルを読まないよう保持しておき、
    /// 設定画面で保存されたときに読み直す。
    /// </summary>
    private AppSettings _cardEffectSettings = AppSettingsLoader.Load();

    /// <summary>BUG-8: Agent/Task命令文からのメンバー名検出用に、これまでに読み込んだメンバー(Id→名前情報)を覚えておく。</summary>
    private readonly KnownMemberCandidateRegistry _knownMemberCandidates = new();

    /// <summary>候補メンバーの全スコープ読み込みの世代番号(古い読み込み結果が新しい結果を上書きしないための判定用)。</summary>
    private int _knownMembersReloadGeneration;
    private string _searchFilter = string.Empty;
    private string _engineFilter = "All";

    /// <summary>F-3: メンバーカードのドラッグ&amp;ドロップ異動用のドラッグ開始位置とドラッグ中フラグ。</summary>
    private Point _cardDragStartPoint;
    private bool _isCardDragging;

    /// <summary>
    /// 最小化ボタン(カスタムタイトルバー)のクリック。ウィンドウを最小化する。
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">イベント引数</param>
    private void TitleBarMinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>
    /// 最大化/復元ボタン(カスタムタイトルバー)のクリック。最大化と通常サイズを切り替える。
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">イベント引数</param>
    private void TitleBarMaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// 閉じるボタン(カスタムタイトルバー)のクリック。ウィンドウを閉じる。
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">イベント引数</param>
    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// ウィンドウ状態の変更に追従する。最大化時は画面外へはみ出す枠の分をルートBorderの余白で補正し、
    /// 最大化ボタンのアイコン(□ / 重なった□)とツールチップも切り替える。
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">イベント引数</param>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        WindowRootBorder.Padding = isMaximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        TitleBarMaximizeIcon.Data = Geometry.Parse(isMaximized
            ? "M2,0 L10,0 L10,8 M0,2 L8,2 L8,10 L0,10 Z"
            : "M0,0 L10,0 L10,10 L0,10 Z");
        TitleBarMaximizeButton.ToolTip = isMaximized ? "元に戻す" : "最大化";
    }

    /// <summary>
    /// ウィンドウのハンドルが作られた直後に、Windows 11のDWMへ「四隅を丸める」設定を依頼する。
    /// 非対応OS(Windows 10以前)では何も起きない(失敗しても無視)。最大化・スナップ時の角の扱いはOSが自動で行う。
    /// </summary>
    /// <param name="e">イベント引数</param>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = WindowCornerPreference;
            _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreferenceAttribute, ref preference, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 角丸を指定できない環境では、従来どおり四角の窓のまま動かす
        }
    }

    /// <summary>
    /// 右クリックメニュー/通知メニューに、角丸のスタイル(メニュー本体・項目・区切り線)を適用する。
    /// 項目を追加し終えた後に呼ぶ。
    /// </summary>
    /// <param name="menu">スタイルを適用するメニュー。</param>
    private void ApplyRoundedMenuStyle(ContextMenu menu)
    {
        menu.Style = (Style)FindResource("RoundedContextMenuStyle");
        var itemStyle = (Style)FindResource("RoundedMenuItemStyle");
        var separatorStyle = (Style)FindResource("RoundedMenuSeparatorStyle");
        foreach (var item in menu.Items)
        {
            switch (item)
            {
                case MenuItem menuItem:
                    menuItem.Style = itemStyle;
                    break;
                case Separator separator:
                    separator.Style = separatorStyle;
                    break;
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += MainWindow_StateChanged;

        _currentScope = CreateDepartmentScope();

        SetupBrandAvatars();
        LoadRegisteredGroups();
        ReloadSidebar();
        SelectScope(_currentScope);
        _activityMonitor.GeminiScanner = GeminiSessionScanner.ScanActiveSessions;
        ApplyRecentConversationSettings();
        _activityMonitor.NominationCandidatesProvider = () => _knownMemberCandidates.Snapshot();
        StartActivityMonitoring();
        HandleScreenshotArgument();
        _ = RefreshCliAvailabilityAsync();
    }

    /// <summary>ロゴ丸に表示するナマケモノの切り出し範囲(元画像に対する相対値 0〜1)。X</summary>
    private const double LogoViewboxX = 0.2;
    /// <summary>ロゴ丸の切り出し範囲 Y</summary>
    private const double LogoViewboxY = 0.23125;
    /// <summary>ロゴ丸の切り出し範囲 幅・高さ(正方形で縦横比を保つ)</summary>
    private const double LogoViewboxSize = 0.5;

    /// <summary>
    /// サイドバー・タイトルバーのロゴ丸にナマケモノ画像を設定します。
    /// 余白を除きナマケモノ本体を大きく見せるため Viewbox で切り出します(縦横比は維持)。
    /// </summary>
    private void SetupBrandAvatars()
    {
        var slothWarmAsset = Path.Combine(AppContext.BaseDirectory, "Assets", "icon_sloth_warm.png");
        if (File.Exists(slothWarmAsset))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(slothWarmAsset, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var brush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Viewbox = new Rect(LogoViewboxX, LogoViewboxY, LogoViewboxSize, LogoViewboxSize)
                };
                SidebarLogoBorder.Background = brush;
                TitleBarLogoBorder.Background = brush;
            }
            catch
            {
                // 画像読み込み失敗時はフォールバック
            }
        }
    }

    private void HandleScreenshotArgument()
    {
        var args = Environment.GetCommandLineArgs();
        var screenshotIndex = Array.IndexOf(args, "--screenshot");
        if (screenshotIndex >= 0 && screenshotIndex + 1 < args.Length)
        {
            var outputPath = args[screenshotIndex + 1];
            var isMaximized = args.Contains("--maximized");
            if (isMaximized)
            {
                WindowState = WindowState.Maximized;
            }

            Loaded += async (_, _) =>
            {
                try
                {
                    // UIレイアウトとセッション・メンバー読み込みの完了を待機
                    await System.Threading.Tasks.Task.Delay(2000);
                    UpdateLayout();

                    // 等倍（1:1）ピクセル完全一致のネイティブキャプチャ（引き伸ばし拡大を行わない）
                    var pixelWidth = Math.Max(1, (int)ActualWidth);
                    var pixelHeight = Math.Max(1, (int)ActualHeight);

                    var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(this);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = File.Create(outputPath);
                    encoder.Save(fs);
                }
                finally
                {
                    Application.Current.Shutdown();
                }
            };
        }
    }

    // ---- 状態監視 (MemberActivityMonitor) ----

    private void StartActivityMonitoring()
    {
        var settings = AppSettingsLoader.Load();
        DiagnosticLog.Shared.Write($"監視開始 sessionRefresh={settings.SessionRefreshIntervalSeconds}s logPoll={settings.LogPollIntervalSeconds}s minRunning={settings.EffectiveMinRunningDisplaySeconds}s");

        var sessionRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(settings.SessionRefreshIntervalSeconds) };
        sessionRefreshTimer.Tick += (_, _) =>
        {
            // Q-1: 未ログイン等でCLI起動がハングし前回の更新が終わっていない間、
            // 30秒ごとにcmd+nodeプロセスを増殖させないための二重実行防止ガード。
            if (Interlocked.CompareExchange(ref _isSessionRefreshing, 1, 0) != 0)
            {
                return;
            }

            _ = RunMonitorTickAsync(async () =>
            {
                try
                {
                    // 改善4/5: 解決済みCLIパスを使い、プロセス起動・出力待機を非同期で行いUIスレッドをブロックしない。
                    var cliPath = AppSettingsLoader.Load().EffectiveClaudeCliPath;
                    var sessions = await MemberActivityMonitor.RunAgentsJsonCommandAsync(cliPath);
                    _activityMonitor.RefreshBusySessions(sessions);
                    RefreshLeaderBanner();
                    RefreshActiveLeaders();
                    RefreshMemberCards();
                    RefreshDashboardActivity(reloadEntries: true);
                }
                finally
                {
                    Interlocked.Exchange(ref _isSessionRefreshing, 0);
                }
            });
        };
        sessionRefreshTimer.Start();

        var logPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(settings.LogPollIntervalSeconds) };
        logPollTimer.Tick += (_, _) => RunMonitorTick(() =>
        {
            _activityMonitor.PollTrackedSessions();
            ApplyActivityBadges();
            RefreshDashboardActivity(reloadEntries: false);
            RefreshActiveLeaders();
            RefreshLeaderBanner();
        });
        logPollTimer.Start();

        _ = RunMonitorTickAsync(async () =>
        {
            var sessionsAtStartup = await MemberActivityMonitor.RunAgentsJsonCommandAsync(settings.EffectiveClaudeCliPath);
            _activityMonitor.RefreshBusySessions(sessionsAtStartup);
            RefreshLeaderBanner();
            RefreshActiveLeaders();
        });
    }

    /// <summary>
    /// 監視タイマー1周期分の処理を実行する。途中で例外が出ても握りつぶして次の周期へ進む(BUG-1)。
    /// これが無いと、例外がDispatcherUnhandledExceptionに流れて5秒ごとにエラーダイアログが出続けたり、
    /// その周期の残り処理(バッジ更新など)が丸ごと飛んで画面の状態が止まって見える。
    /// </summary>
    /// <param name="tick">1周期分の処理。</param>
    private static void RunMonitorTick(Action tick)
    {
        try
        {
            tick();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] 監視周期で例外(次周期で再試行): {ex}");
        }
    }

    /// <summary>
    /// <see cref="RunMonitorTick"/>の非同期版(改善5)。CLIプロセスの起動・出力待機など
    /// UIスレッドをブロックしうる処理を含む1周期分の処理を実行する。例外は同様に握りつぶし、
    /// 次周期での再試行に委ねる(BUG-1と同じ方針)。
    /// </summary>
    /// <param name="tick">1周期分の非同期処理。</param>
    private static async Task RunMonitorTickAsync(Func<Task> tick)
    {
        try
        {
            await tick();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] 監視周期で例外(次周期で再試行): {ex}");
        }
    }

    /// <summary>
    /// 読み込んだメンバーを命令文からのメンバー名検出用の既知メンバー一覧に登録する(BUG-8)。
    /// </summary>
    /// <param name="agents">読み込み済みのエージェント定義。</param>
    private void RememberKnownMembers(IEnumerable<AgentDefinition> agents)
    {
        _knownMemberCandidates.Remember(agents);
    }

    /// <summary>
    /// 全スコープ(全社共通・全ワークスペース・全チーム)のメンバーを裏で読み込み、検知用の候補を入れ替える。
    /// 起動直後や、一度も開いていないチームのメンバーも作業中・完了を検知できるようにする。
    /// ファイル読み込みはバックグラウンドで行いUIをブロックしない。結果の反映はUIスレッドで行い、
    /// 後から始めた読み込みの結果を優先する(古い結果で上書きしない)。
    /// </summary>
    private async void ReloadKnownMembersInBackground()
    {
        var generation = Interlocked.Increment(ref _knownMembersReloadGeneration);
        try
        {
            // スコープの一覧(UIが持つ_groups)はUIスレッドで写し取り、ファイル読み込みだけ裏で行う
            var scopes = BuildStudioScopeSpecs();
            var entries = await Task.Run(() => LoadStudioAgents(scopes));
            if (generation == Volatile.Read(ref _knownMembersReloadGeneration))
            {
                _dashboardEntries = entries;
                _knownMemberCandidates.Replace(entries.Select(e => e.Agent));
                if (_currentScope.Kind == ScopeKind.Department)
                {
                    RefreshDashboardActivity(reloadEntries: false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] 候補メンバーの全スコープ読み込みで例外(次周期で再試行): {ex.Message}");
        }
    }

    /// <summary>
    /// 各メンバーカードの表示(状態バッジ・作業中の揺れ・完了後の光)を最新の稼働状態に合わせ、
    /// 完了(Running→Done)を検知したら完了通知(通知ベル・光・アバターの「ぷるん」・通知音・タスクバー点滅)を行う。
    /// 揺れや光は「すでに出ていればそのまま」にするため、周期ごとに作り直してカクつくことはない。
    /// </summary>
    private void ApplyActivityBadges()
    {
        // 完了通知の設定は完了を検知したときだけ読み込む(毎周期のファイル読み込みを避ける)
        AppSettings? notifySettings = null;
        var cards = FindMemberCards(this).Where(c => c.Tag is AgentDefinition).ToList();

        // 同じメンバーのカードが複数のセクションに出ていても、完了の検知と通知はメンバーごとに1回にする
        var states = new Dictionary<string, MemberActivityState>(StringComparer.Ordinal);
        var completedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            var agent = (AgentDefinition)card.Tag;
            if (states.ContainsKey(agent.Name))
            {
                continue;
            }

            var state = _activityMonitor.GetState(agent.Name);
            states[agent.Name] = state;
            if (_cardAttention.Observe(agent.Name, state))
            {
                completedNames.Add(agent.Name);
                AddNotification(
                    $"🎉 タスク完了: {agent.EffectiveDisplayName}",
                    $"{agent.EffectiveDisplayName} の作業が完了しました。");
            }
        }

        if (completedNames.Count > 0)
        {
            notifySettings = AppSettingsLoader.Load();
        }

        foreach (var card in cards)
        {
            var agent = (AgentDefinition)card.Tag;
            ApplyCardPresentation(card, agent, states[agent.Name]);

            if (notifySettings is not null && notifySettings.AvatarBounceEnabled && completedNames.Contains(agent.Name))
            {
                PlayAvatarBounce(card, notifySettings);
            }
        }

        // 画面にカードが無いメンバー(他のチームの人など)も、Dashboardの上部エリアで完了を取りこぼさないよう
        // 状態だけ観測しておく(通知・演出は、カードが画面にあるメンバーだけの既存の動きのまま)
        foreach (var (name, state) in _activityMonitor.States)
        {
            if (!states.ContainsKey(name))
            {
                _cardAttention.Observe(name, state);
            }
        }

        // 同じ周期で複数人が完了しても、音と点滅は1回にまとめる
        if (notifySettings is not null)
        {
            if (notifySettings.CompletionSoundEnabled)
            {
                CompletionSound.Play(notifySettings.CompletionSoundPath);
            }

            if (notifySettings.TaskbarFlashEnabled)
            {
                TaskbarFlasher.FlashIfInactive(this);
            }
        }
    }

    /// <summary>
    /// Dashboard選択時、上部の「稼働中・完了のメンバー」エリアを最新状態に反映する(全チーム横断)。
    /// 出すメンバーの判定は <see cref="DashboardActivityCollector"/> に任せ、ここではカードの差分更新だけを行う
    /// (周期ごとに作り直すと揺れや光が途切れるため、同じメンバーのカードは再利用する)。
    /// Dashboard以外が選択されている間はエリアを隠してカードを外す。
    /// </summary>
    /// <param name="reloadEntries">全チームのメンバー一覧をディスクから(バックグラウンドで)読み直すか(falseならキャッシュを使う)。</param>
    private void RefreshDashboardActivity(bool reloadEntries)
    {
        // 全スコープの読み直しは裏で行い(Dashboard用のキャッシュと検知用の候補を同時に更新)、
        // 表示中のスコープに関わらず行う。完了後にDashboardなら表示へ反映する
        if (reloadEntries)
        {
            ReloadKnownMembersInBackground();
        }

        if (_currentScope.Kind != ScopeKind.Department)
        {
            DashboardActivitySection.Visibility = Visibility.Collapsed;
            DashboardActivityCardsPanel.Children.Clear();
            _dashboardCards.Clear();
            return;
        }

        DashboardActivityTitleText.Text = DashboardActivityCollector.ActiveSectionTitle;
        DashboardCommonMembersTitleText.Text = DashboardActivityCollector.CommonMembersSectionTitle;
        DashboardActivityEmptyText.Text = DashboardActivityCollector.EmptyMessage;
        DashboardActivitySection.Visibility = Visibility.Visible;

        var visible = DashboardActivityCollector.Collect(
            _dashboardEntries,
            _activityMonitor.GetState,
            _cardAttention.IsAwaitingAcknowledgement);

        // 出さなくなったメンバー(確認済み・待機に戻った)のカードを外す
        var visibleIds = visible.Select(e => e.Agent.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in _dashboardCards.Keys.Where(id => !visibleIds.Contains(id)).ToList())
        {
            DashboardActivityCardsPanel.Children.Remove(_dashboardCards[staleId].Card);
            _dashboardCards.Remove(staleId);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var entry = visible[i];
            var teamLabel = DashboardActivityCollector.FormatTeamLabel(entry.ScopeLabel);

            // 定義(ファイル)やチーム名が変わっていればカードを作り直し、同じなら再利用する
            Border card;
            if (!_dashboardCards.TryGetValue(entry.Agent.Name, out var existing) ||
                existing.Card.Tag is not AgentDefinition cached ||
                cached != entry.Agent ||
                existing.TeamLabel != teamLabel)
            {
                if (existing.Card is not null)
                {
                    DashboardActivityCardsPanel.Children.Remove(existing.Card);
                }

                card = BuildVerticalStudioCard(entry.Agent, entry.ScopeLabel, teamLabel, entry.ClaudeDir);
                _dashboardCards[entry.Agent.Name] = (card, teamLabel);
                DashboardActivityCardsPanel.Children.Add(card);
            }
            else
            {
                card = existing.Card;
            }

            var currentIndex = DashboardActivityCardsPanel.Children.IndexOf(card);
            if (currentIndex != i)
            {
                DashboardActivityCardsPanel.Children.Remove(card);
                DashboardActivityCardsPanel.Children.Insert(i, card);
            }
        }

        DashboardActivityEmptyBorder.Visibility = DashboardActivityCollector.ShouldShowEmptyMessage(visible.Count)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// 1枚のメンバーカードの表示(状態バッジ・作業中の揺れ・完了後の光)を、指定の状態に合わせる。
    /// 何度呼んでも同じ結果になる(すでに揺れている・光っているカードはそのまま続ける)。
    /// 状態監視の周期のほか、カードが画面に載ったとき(再生成・チーム切り替え)にも呼んで表示を復元する。
    /// </summary>
    /// <param name="card">メンバーカード。</param>
    /// <param name="agent">カードのメンバー。</param>
    /// <param name="state">メンバーの現在の稼働状態。</param>
    private void ApplyCardPresentation(Border card, AgentDefinition agent, MemberActivityState state)
    {
        UpdateStateBadge(card, MemberCardAttentionTracker.GetBadgeKind(state));

        var avatar = FindChild<FrameworkElement>(card, AvatarVisualTag);
        if (avatar is not null)
        {
            if (MemberCardAttentionTracker.ShouldSway(state, _cardEffectSettings.RunningSwayEnabled))
            {
                AvatarSwayAnimation.Start(
                    avatar,
                    _cardEffectSettings.EffectiveRunningSwayAngleDegrees,
                    _cardEffectSettings.EffectiveRunningSwayPeriodMilliseconds,
                    _cardEffectSettings.EffectiveRunningSwayBreathAmplitude,
                    CardHeight / CardWidth);
            }
            else
            {
                AvatarSwayAnimation.Stop(avatar, smooth: true);
            }
        }

        if (_cardAttention.IsAwaitingAcknowledgement(agent.Name))
        {
            ShowCompletionGlow(card);
        }
        else
        {
            HideCompletionGlow(card);
        }
    }

    /// <summary>
    /// 状態バッジの表示を切り替える。完了(Done)はバッジに出さず Idle 表示にする(光で知らせるため、ユーザー決定)。
    /// </summary>
    /// <param name="card">メンバーカード。</param>
    /// <param name="kind">バッジに出す表示の種類。</param>
    private static void UpdateStateBadge(Border card, MemberCardBadgeKind kind)
    {
        // バッジ要素を探す（cardの中のBorderでTag="StateBadge"を持つもの）
        var stateBadge = FindChild<Border>(card, "StateBadge");
        var stateDot = FindChild<Ellipse>(card, "StateDot");
        var stateLabel = FindChild<TextBlock>(card, "StateLabel");
        if (stateBadge is null || stateDot is null || stateLabel is null)
        {
            return;
        }

        var (dotBrush, textBrush, bgBrush, label) = kind switch
        {
            MemberCardBadgeKind.Active => (
                Brushes.DodgerBlue,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#1D4ED8")!,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#DBEAFE")!,
                "Active"),
            MemberCardBadgeKind.Cancelled => (
                (SolidColorBrush)new BrushConverter().ConvertFromString("#F59E0B")!,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#B45309")!,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#FEF3C7")!,
                "中断"),
            MemberCardBadgeKind.TimedOut => (
                (SolidColorBrush)new BrushConverter().ConvertFromString("#F59E0B")!,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#B45309")!,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#FEF3C7")!,
                "Timeout"),
            _ => (
                (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#475569")!,
                (SolidColorBrush)new BrushConverter().ConvertFromString("#F1F5F9")!,
                "Idle"),
        };

        // 同じ表示なら触らない(周期ごとの無駄な再描画を避ける)
        if (stateLabel.Text == label)
        {
            return;
        }

        stateDot.Fill = dotBrush;
        stateLabel.Text = label;
        stateLabel.Foreground = textBrush;
        stateBadge.Background = bgBrush;
    }

    /// <summary>
    /// 完了したカードのアバターを「ぷるん」と伸び縮みさせる(設定した回数くり返す)。
    /// アバター要素が見つからない場合は何もしない。
    /// </summary>
    /// <param name="card">完了したメンバーカード。</param>
    /// <param name="settings">演出の長さ・強さ・回数を含む設定値。</param>
    private static void PlayAvatarBounce(Border card, AppSettings settings)
    {
        var avatar = FindChild<FrameworkElement>(card, AvatarVisualTag);
        if (avatar is null)
        {
            return;
        }

        AvatarBounceAnimation.Play(
            avatar,
            settings.EffectiveCompletionBounceDurationMilliseconds,
            settings.EffectiveCompletionBounceAmplitude,
            settings.EffectiveCompletionBounceCount);
    }

    /// <summary>
    /// 完了したカードを光らせる(太めの枠＋外側のぼんやりした光。設定によりゆっくり明滅)。
    /// クリックで確認されるまで消さない。すでに光っていれば何もしない。
    /// </summary>
    /// <param name="card">光らせるメンバーカード。</param>
    private void ShowCompletionGlow(Border card)
    {
        // すでに光っていれば何もしない(先に判定して、周期ごとの要素探索を省く)
        if (CardGlowEffect.IsShown(card) || FindChild<Border>(card, GlowFrameTag) is not { } frame)
        {
            return;
        }

        var color = (Color)ColorConverter.ConvertFromString(_cardEffectSettings.EffectiveCompletionGlowColor)!;
        CardGlowEffect.Show(
            card,
            frame,
            color,
            _cardEffectSettings.EffectiveCompletionGlowThickness,
            _cardEffectSettings.EffectiveCompletionGlowBlurRadius,
            _cardEffectSettings.EffectiveCompletionGlowPulseMilliseconds);
    }

    /// <summary>
    /// カードの光を消し、枠線を通常の色(マウスが乗っていればホバー色)に戻す。光っていなければ何もしない。
    /// </summary>
    /// <param name="card">光を消すメンバーカード。</param>
    private static void HideCompletionGlow(Border card)
    {
        // 光っていなければ何もしない(先に判定して、周期ごとの要素探索を省く)
        if (!CardGlowEffect.IsShown(card) || FindChild<Border>(card, GlowFrameTag) is not { } frame)
        {
            return;
        }

        CardGlowEffect.Hide(card, frame);
        card.BorderBrush = card.IsMouseOver ? CardBorderHoverAccent : CardBorderDefault;
    }

    /// <summary>
    /// 指定メンバーのカード(複数のセクションに出ている場合はすべて)の表示を、現在の状態で更新する。
    /// 光っているカードをクリックして確認したときに、同じメンバーの他のカードの光も消すために使う。
    /// </summary>
    /// <param name="memberName">メンバー名(エージェントのId)。</param>
    private void RefreshCardPresentationsFor(string memberName)
    {
        foreach (var card in FindMemberCards(this))
        {
            if (card.Tag is AgentDefinition agent && agent.Name == memberName)
            {
                ApplyCardPresentation(card, agent, _activityMonitor.GetState(agent.Name));
            }
        }
    }

    /// <summary>
    /// 設定の変更(揺れのオン/オフ・角度、光の色、リーダーのキャラ名など)を表示中のすべてのカード(メンバー・リーダー)に反映する。
    /// 揺れと光はいったん止めてから、新しい設定で出し直す。
    /// </summary>
    private void ReapplyAllCardEffects()
    {
        foreach (var card in FindMemberCards(this))
        {
            if (card.Tag is not AgentDefinition agent)
            {
                continue;
            }

            SuspendCardEffects(card);
            ApplyCardPresentation(card, agent, _activityMonitor.GetState(agent.Name));
        }

        // リーダー(相棒)カードも同じ演出なので出し直す(キャラ名の変更もここで反映する)
        foreach (var refs in _leaderCards.Values)
        {
            if (!_leaderSummaries.TryGetValue(refs.Engine, out var summary))
            {
                continue;
            }

            SuspendCardEffects(refs.Card);
            UpdateLeaderCard(refs, summary);
        }
    }

    /// <summary>
    /// カードの揺れと光のアニメーションを即座に止める(カードが画面から外れたとき等)。
    /// 止まったアニメーションが画面外のカードを握り続けないようにするため。
    /// 「完了未確認」の情報は消さないので、カードが画面に戻れば <see cref="ApplyCardPresentation"/> で復元される。
    /// </summary>
    /// <param name="card">メンバーカード。</param>
    private static void SuspendCardEffects(Border card)
    {
        var avatar = FindChild<FrameworkElement>(card, AvatarVisualTag);
        if (avatar is not null)
        {
            AvatarSwayAnimation.Stop(avatar, smooth: false);
        }

        HideCompletionGlow(card);
    }

    private static T? FindChild<T>(DependencyObject parent, string childTag) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Tag as string == childTag)
            {
                return fe;
            }

            var found = FindChild<T>(child, childTag);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static IEnumerable<Border> FindMemberCards(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject childObject)
            {
                continue;
            }

            if (childObject is Border { Tag: AgentDefinition } border)
            {
                yield return border;
            }

            foreach (var nested in FindMemberCards(childObject))
            {
                yield return nested;
            }
        }
    }

    // ---- グループ・チームの読み込みとサイドバー構築 ----

    private void LoadRegisteredGroups()
    {
        _groups.Clear();
        foreach (var path in GroupRegistry.LoadWorkspaces(GlobalClaudeMdPath))
        {
            if (Directory.Exists(path))
            {
                var groupItem = new GroupItem
                {
                    RootPath = path,
                    DisplayName = ExtractName(Path.Combine(path, "CLAUDE.md"), path),
                };

                foreach (var folder in Directory.EnumerateDirectories(path).OrderBy(f => f))
                {
                    if (IsTeamFolder(folder))
                    {
                        var teamClaudeMd = Path.Combine(folder, "CLAUDE.md");
                        groupItem.Teams.Add(new TeamItem
                        {
                            RootPath = folder,
                            DisplayName = ExtractName(teamClaudeMd, folder),
                            ParentGroupPath = path,
                        });
                    }
                }

                _groups.Add(groupItem);
            }
        }
    }

    private static bool IsTeamFolder(string folderPath)
    {
        if (File.Exists(Path.Combine(folderPath, "CLAUDE.md")) || File.Exists(Path.Combine(folderPath, "GEMINI.md")))
        {
            return true;
        }

        var agentsDir = Path.Combine(folderPath, ".claude", "agents");
        if (Directory.Exists(agentsDir) && Directory.EnumerateFiles(agentsDir, "*.md").Any())
        {
            return true;
        }

        var skillsDir = Path.Combine(folderPath, ".agents", "skills");
        return Directory.Exists(skillsDir) && Directory.EnumerateDirectories(skillsDir).Any();
    }

    // 再構築の予約済みフラグ。開閉ボタンの高速連打を1回の再構築にまとめる(BUG-6)。
    private bool _sidebarReloadPending;

    /// <summary>
    /// サイドバーの再構築を予約する。同じ周期内に何度呼ばれても再構築は1回だけで、
    /// 予約時点ではなく実行時点の最新状態(折りたたみ集合・選択スコープ)から組み立てる。
    /// 開閉ボタンのClickハンドラ内でそのボタン自身を破棄・再生成すると、連打時に破棄済みボタンへの
    /// クリックや選択ハイライトの取りこぼしが起きるため、Clickの処理が終わってから再構築する (BUG-6)。
    /// </summary>
    private void RequestSidebarReload()
    {
        if (_sidebarReloadPending)
        {
            return;
        }

        _sidebarReloadPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _sidebarReloadPending = false;
            ReloadSidebar();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ReloadSidebar()
    {
        _navButtons.Clear();
        _navButtons.Add(DepartmentNavButton);

        DepartmentNavButton.Tag = _currentScope.Kind == ScopeKind.Department ? "Selected" : null;

        WorkspaceTreePanel.Children.Clear();

        if (_groups.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "ワークスペース未登録\n上の「＋」から追加",
                FontSize = 11.5,
                Foreground = (SolidColorBrush)FindResource("SidebarMutedTextBrush"),
                Margin = new Thickness(8, 8, 8, 8),
                TextWrapping = TextWrapping.Wrap,
            };
            WorkspaceTreePanel.Children.Add(emptyText);
            return;
        }

        foreach (var group in _groups)
        {
            // F-4: グループ単位のまとまりを視覚的に区切るため、淡い背景パネルで包む。
            var groupContainer = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var groupHeaderGrid = new Grid();
            // F-2: 開閉シェブロン(▼/▶)列を先頭に追加。配下チームが無いグループはシェブロン非表示で幅だけ確保する。
            groupHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            groupHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            groupHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var isGroupSelected = _currentScope.Kind == ScopeKind.Group && _currentScope.FolderPath == group.RootPath;
            // 選択中のチームがこのグループ配下にある場合は、折りたたみ設定に関わらず強制的に展開する
            // (選択中のチームがサイドバーから見えなくなってしまう違和感を防ぐ)。
            var hasSelectedTeamInside = _currentScope.Kind == ScopeKind.Team && _currentScope.ParentGroupPath == group.RootPath;
            var isCollapsed = _collapsedGroups.Contains(group.RootPath) && !hasSelectedTeamInside;
            var currentGroup = group;

            // F-2: 開閉トグルボタン(▼展開中 / ▶折りたたみ中)。チームが無いグループには表示しない。
            var chevronBtn = new Button
            {
                Style = (Style)FindResource("SidebarIconButton"),
                Width = 20,
                Height = 24,
                Margin = new Thickness(0, 0, 2, 0),
                Visibility = group.Teams.Count > 0 ? Visibility.Visible : Visibility.Hidden,
                ToolTip = isCollapsed ? "配下チームを展開" : "配下チームを折りたたむ",
                Content = new TextBlock
                {
                    Text = isCollapsed ? "▶" : "▼",
                    FontSize = 9,
                    Foreground = (SolidColorBrush)FindResource("SidebarMutedTextBrush"),
                },
            };
            chevronBtn.Click += (_, _) =>
            {
                if (!_collapsedGroups.Remove(currentGroup.RootPath))
                {
                    _collapsedGroups.Add(currentGroup.RootPath);
                }
                RequestSidebarReload();
            };
            Grid.SetColumn(chevronBtn, 0);
            groupHeaderGrid.Children.Add(chevronBtn);

            var groupBtn = new Button
            {
                Style = (Style)FindResource("SidebarNavButton"),
                Tag = isGroupSelected ? "Selected" : null,
                ToolTip = group.RootPath,
            };

            var groupContent = new StackPanel { Orientation = Orientation.Horizontal };
            groupContent.Children.Add(new TextBlock { Text = "📁", Margin = new Thickness(0, 0, 8, 0), FontSize = 12.5 });
            groupContent.Children.Add(new TextBlock
            {
                Text = group.DisplayName,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (group.Teams.Count > 0)
            {
                // F-4: 配下チーム数を添えて、グループ配下の規模が一目でわかるようにする。
                groupContent.Children.Add(new TextBlock
                {
                    Text = $" ({group.Teams.Count})",
                    FontSize = 11,
                    Foreground = (SolidColorBrush)FindResource("SidebarMutedTextBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            groupBtn.Content = groupContent;

            groupBtn.Click += (_, _) =>
            {
                SelectScope(new ScopeContext(
                    ScopeKind.Group,
                    currentGroup.DisplayName,
                    currentGroup.RootPath,
                    null,
                    Path.Combine(currentGroup.RootPath, "CLAUDE.md")));
            };

            // ワークスペースの右クリックメニュー (エクスプローラー、チーム作成、登録解除)
            var groupMenu = new ContextMenu();
            var openExplorerItem = new MenuItem { Header = "📂 エクスプローラーで開く" };
            openExplorerItem.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = currentGroup.RootPath,
                        UseShellExecute = true,
                    });
                }
                catch { }
            };
            groupMenu.Items.Add(openExplorerItem);

            var addTeamMenuItem = new MenuItem { Header = "👥 新しいチームを作成..." };
            addTeamMenuItem.Click += (_, _) => AddTeamToGroup(currentGroup);
            groupMenu.Items.Add(addTeamMenuItem);

            groupMenu.Items.Add(new Separator());

            var unregisterItem = new MenuItem { Header = "➖ ワークスペースの登録を解除..." };
            unregisterItem.Click += (_, _) => UnregisterWorkspace(currentGroup);
            groupMenu.Items.Add(unregisterItem);

            ApplyRoundedMenuStyle(groupMenu);
            groupBtn.ContextMenu = groupMenu;

            // F-3: メンバーカードのドロップ先(グループ直下への異動)
            groupBtn.AllowDrop = true;
            groupBtn.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(typeof(AgentDefinition)) ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            groupBtn.Drop += (_, e) => HandleMemberDropOnGroup(e, currentGroup);

            _navButtons.Add(groupBtn);
            Grid.SetColumn(groupBtn, 1);
            groupHeaderGrid.Children.Add(groupBtn);

            var addTeamBtn = new Button
            {
                Style = (Style)FindResource("SidebarIconButton"),
                ToolTip = $"「{group.DisplayName}」にチームを追加",
                Margin = new Thickness(2, 0, 4, 0),
            };
            addTeamBtn.Content = new TextBlock { Text = "＋", FontSize = 12, FontWeight = FontWeights.Bold };
            addTeamBtn.Click += (_, _) => AddTeamToGroup(currentGroup);
            Grid.SetColumn(addTeamBtn, 2);
            groupHeaderGrid.Children.Add(addTeamBtn);

            groupContainer.Children.Add(groupHeaderGrid);

            // F-2: 折りたたみ中は配下チームリストそのものを描画しない(アコーディオン)。
            if (group.Teams.Count > 0 && !isCollapsed)
            {
                // F-4: 左に縦のガイド線を添えて階層関係を視覚的に分かりやすくする。
                var teamListContainer = new Border
                {
                    BorderBrush = (SolidColorBrush)FindResource("SidebarBorderBrush"),
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Margin = new Thickness(11, 2, 0, 4),
                    Padding = new Thickness(8, 0, 0, 0),
                };
                var teamListPanel = new StackPanel();
                teamListContainer.Child = teamListPanel;
                foreach (var team in group.Teams)
                {
                    var isTeamSelected = _currentScope.Kind == ScopeKind.Team && _currentScope.FolderPath == team.RootPath;
                    var isIsolated = team.IsIndependentGitRepo;
                    var teamBtn = new Button
                    {
                        Style = (Style)FindResource("SidebarNavButton"),
                        Tag = isTeamSelected ? "Selected" : null,
                        Padding = new Thickness(10, 6, 8, 6),
                        ToolTip = isIsolated
                            ? $"{team.RootPath}\n\n⚠️ このチームフォルダは独立したGitリポジトリです。\nVSCode等で直接開くと、親ワークスペースのCLAUDE.md/GEMINI.mdが\nCLIから自動継承されず無視されます。"
                            : team.RootPath,
                    };

                    var teamContent = new StackPanel { Orientation = Orientation.Horizontal };
                    teamContent.Children.Add(new TextBlock { Text = "👥", Margin = new Thickness(0, 0, 7, 0), FontSize = 11, Foreground = (SolidColorBrush)FindResource("SidebarAccentBrush") });
                    teamContent.Children.Add(new TextBlock
                    {
                        Text = team.DisplayName,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 175,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    teamBtn.Content = teamContent;

                    var currentTeam = team;
                    teamBtn.Click += (_, _) =>
                    {
                        SelectScope(new ScopeContext(
                            ScopeKind.Team,
                            currentTeam.DisplayName,
                            currentTeam.RootPath,
                            currentTeam.ParentGroupPath,
                            Path.Combine(currentTeam.RootPath, "CLAUDE.md")));
                    };

                    var teamMenu = new ContextMenu();
                    var openTeamExplorerItem = new MenuItem { Header = "📂 エクスプローラーで開く" };
                    openTeamExplorerItem.Click += (_, _) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = currentTeam.RootPath,
                                UseShellExecute = true,
                            });
                        }
                        catch { }
                    };
                    teamMenu.Items.Add(openTeamExplorerItem);
                    ApplyRoundedMenuStyle(teamMenu);
                    teamBtn.ContextMenu = teamMenu;

                    // F-3: メンバーカードのドロップ先(チームへの異動)
                    teamBtn.AllowDrop = true;
                    teamBtn.DragOver += (_, e) =>
                    {
                        e.Effects = e.Data.GetDataPresent(typeof(AgentDefinition)) ? DragDropEffects.Move : DragDropEffects.None;
                        e.Handled = true;
                    };
                    teamBtn.Drop += (_, e) => HandleMemberDropOnTeam(e, currentTeam);

                    _navButtons.Add(teamBtn);
                    teamListPanel.Children.Add(teamBtn);
                }
                groupContainer.Children.Add(teamListContainer);
            }

            WorkspaceTreePanel.Children.Add(groupContainer);
        }
    }

    /// <summary>メンバーカードをワークスペース(グループ)項目にドロップした際の異動処理(F-3)。グループ直下へ異動する。</summary>
    private void HandleMemberDropOnGroup(DragEventArgs e, GroupItem group)
    {
        if (!TryGetDraggedAgent(e, out var agent))
        {
            return;
        }

        var targetClaudeDir = Path.Combine(group.RootPath, ".claude", "agents");
        var targetGeminiDir = Path.Combine(group.RootPath, ".agents", "skills");
        MoveAgentAndRefresh(agent, targetClaudeDir, targetGeminiDir, group.DisplayName);
    }

    /// <summary>メンバーカードをチーム項目にドロップした際の異動処理(F-3)。指定チームへ異動する。</summary>
    private void HandleMemberDropOnTeam(DragEventArgs e, TeamItem team)
    {
        if (!TryGetDraggedAgent(e, out var agent))
        {
            return;
        }

        var targetClaudeDir = Path.Combine(team.RootPath, ".claude", "agents");
        var targetGeminiDir = Path.Combine(team.RootPath, ".agents", "skills");
        MoveAgentAndRefresh(agent, targetClaudeDir, targetGeminiDir, team.DisplayName);
    }

    private static bool TryGetDraggedAgent(DragEventArgs e, out AgentDefinition agent)
    {
        if (e.Data.GetDataPresent(typeof(AgentDefinition)) && e.Data.GetData(typeof(AgentDefinition)) is AgentDefinition dragged)
        {
            agent = dragged;
            return true;
        }

        agent = null!;
        return false;
    }

    /// <summary>
    /// エージェント定義ファイル(および対応するGeminiスキル)の実移動を行い、成功時はUIを再読み込みして
    /// 通知を出す。移動先に同名の定義が既にある等の理由で失敗した場合はエラーダイアログを表示する(F-3)。
    /// </summary>
    private void MoveAgentAndRefresh(AgentDefinition agent, string targetClaudeDir, string targetGeminiDir, string targetLabel)
    {
        try
        {
            // BUG-18: 移動先にIdまたは呼び名が同じメンバーが既にいる場合は、何も動かさず分かりやすく通知する。
            // (自分自身は除外されるため、同位置へのドロップはここでは衝突扱いにならない)
            var conflict = AgentNameUniqueness.FindConflict(
                agent.Name, agent.DisplayName, AgentNameUniqueness.InScope(GetAllStudioAgents(), targetClaudeDir), agent);
            if (conflict is not null)
            {
                MessageBox.Show(
                    this,
                    $"移動先「{targetLabel}」に同名のエージェント「{conflict.Existing.Agent.EffectiveDisplayName}」が既に存在するため移動できません。\n\n" +
                    $"{conflict.ToMessage()}\nどちらかの名前を変更してから、もう一度移動してください。",
                    "異動できません",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 同位置へのドロップは何も移動されないため、通知も再読み込みも行わない (BUG-10)
            if (!AgentDefinitionLoader.MoveAgent(agent, targetClaudeDir, targetGeminiDir))
            {
                return;
            }

            AddNotification("👥 メンバー異動", $"{agent.EffectiveDisplayName} を「{targetLabel}」へ異動しました。");
            SelectScope(_currentScope);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"メンバーの異動に失敗しました:\n{ex.Message}", "異動エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"メンバーの異動中に予期しないエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// スタジオ全体(全社・全ワークスペース・全チーム)のメンバーを所属先ラベル付きで読み込む(BUG-18)。
    /// 保存・取込・異動・複製の直前に呼んで、Id/呼び名の重複判定に使う。
    /// </summary>
    private IReadOnlyList<StudioAgentEntry> GetAllStudioAgents() => LoadStudioAgents(BuildStudioScopeSpecs());

    /// <summary>メンバー読み込み対象のスコープ1件分(所属先ラベル・Claude/Geminiの各フォルダ・種別)。</summary>
    private sealed record StudioScopeSpec(string Label, string ClaudeDir, string GeminiDir, AgentScope Scope);

    /// <summary>
    /// 読み込み対象の全スコープ(全社共通・全ワークスペース直下・全チーム)の一覧を作る。
    /// UIが持つ_groupsを参照するためUIスレッドで呼ぶ(ファイルは読まない)。
    /// </summary>
    private List<StudioScopeSpec> BuildStudioScopeSpecs()
    {
        var specs = new List<StudioScopeSpec>
        {
            new(DashboardActivityCollector.DashboardName, GlobalAgentsDir, GlobalGeminiSkillsDir, AgentScope.Global)
        };
        foreach (var group in _groups)
        {
            specs.Add(new StudioScopeSpec(
                group.DisplayName,
                Path.Combine(group.RootPath, ".claude", "agents"),
                Path.Combine(group.RootPath, ".agents", "skills"),
                AgentScope.Group));
            foreach (var team in group.Teams)
            {
                specs.Add(new StudioScopeSpec(
                    $"{group.DisplayName} / {team.DisplayName}",
                    Path.Combine(team.RootPath, ".claude", "agents"),
                    Path.Combine(team.RootPath, ".agents", "skills"),
                    AgentScope.Team));
            }
        }

        return specs;
    }

    /// <summary>
    /// スコープの一覧からメンバー定義を読み込む(ファイルI/Oのみでスレッドを選ばない)。
    /// </summary>
    /// <param name="scopes">読み込み対象のスコープ一覧。</param>
    private static IReadOnlyList<StudioAgentEntry> LoadStudioAgents(IEnumerable<StudioScopeSpec> scopes)
    {
        var entries = new List<StudioAgentEntry>();
        foreach (var spec in scopes)
        {
            foreach (var agent in AgentDefinitionLoader.LoadScopeAgents(spec.ClaudeDir, spec.GeminiDir, spec.Scope))
            {
                entries.Add(new StudioAgentEntry(agent, spec.Label, spec.ClaudeDir));
            }
        }

        return entries;
    }

    /// <summary>
    /// 指定フォルダ(同じチーム/グループ内)に所属する既存メンバーを取得するプロバイダ(BUG-18/Q-5改修)。
    /// 他グループの同名メンバーは重複チェックの対象外とする。
    /// </summary>
    private Func<IReadOnlyList<StudioAgentEntry>> GetScopeAgentsProvider(string? claudeDir) =>
        () => string.IsNullOrWhiteSpace(claudeDir)
            ? []
            : AgentNameUniqueness.InScope(GetAllStudioAgents(), claudeDir).ToList();

    /// <summary>
    /// 既存データにIdまたは呼び名の重複があれば、クラッシュさせずに警告バナーとカードの⚠️バッジで知らせる(BUG-18の既存データ救済)。
    /// 重複の内容が前回と変わった時だけ通知ベルにも記録する。
    /// </summary>
    private void RefreshDuplicateWarning()
    {
        IReadOnlyList<DuplicateGroup> duplicates;
        try
        {
            duplicates = AgentNameUniqueness.FindDuplicatesInSameScope(GetAllStudioAgents());
        }
        catch (Exception)
        {
            // 警告表示のための走査が失敗しても、通常の画面描画は続行する
            return;
        }

        _duplicateFilePaths = duplicates
            .SelectMany(d => d.Members)
            .Select(m => m.Agent.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (duplicates.Count == 0)
        {
            DuplicateWarningBorder.Visibility = Visibility.Collapsed;
            _lastDuplicateSignature = string.Empty;
            return;
        }

        var lines = duplicates.Select(d =>
            $"・{(d.Kind == NameConflictKind.Id ? "Id" : "呼び名")}「{d.Key}」: " +
            string.Join(" / ", d.Members.Select(m => $"{m.Agent.EffectiveDisplayName}({m.ScopeLabel})"))).ToList();

        DuplicateWarningDetailText.Text = string.Join("\n", lines);
        DuplicateWarningBorder.Visibility = Visibility.Visible;

        var signature = string.Join("|", lines);
        if (signature != _lastDuplicateSignature)
        {
            _lastDuplicateSignature = signature;
            AddNotification("⚠️ 同名メンバーを検出", $"{duplicates.Count} 件の重複があります。名前を変更してください。");
        }
    }

    private void UpdateNavButtonSelections()
    {
        foreach (var btn in _navButtons)
        {
            btn.Tag = null;
        }

        if (_currentScope.Kind == ScopeKind.Department)
        {
            DepartmentNavButton.Tag = "Selected";
        }
        else
        {
            ReloadSidebar();
        }
    }

    // ---- スコープ切り替えとメインキャンバス描画 ----

    /// <summary>ルールボタンの最大幅(長い名前は末尾を … で省略する)。</summary>
    private const double RuleButtonMaxWidth = 320;

    /// <summary>
    /// ルールボタンの中身(末尾省略付きのテキスト)を作る。文言は ScopeRuleLabelFormatter が決める。
    /// </summary>
    /// <param name="scope">選択中のスコープ。</param>
    /// <returns>ボタンに設定する TextBlock。</returns>
    private TextBlock BuildRuleButtonContent(ScopeContext scope)
    {
        var kind = scope.Kind switch
        {
            ScopeKind.Group => ScopeRuleKind.Group,
            ScopeKind.Team => ScopeRuleKind.Team,
            _ => ScopeRuleKind.Department
        };
        ManageTeamButton.MaxWidth = RuleButtonMaxWidth;
        return new TextBlock
        {
            Text = ScopeRuleLabelFormatter.Format(kind, scope.DisplayName),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    private void SelectScope(ScopeContext scope)
    {
        _currentScope = scope;
        UpdateNavButtonSelections();

        // 1. パンくずナビ更新 (A案モックアップ: Home / Design Team / Subagents)
        switch (scope.Kind)
        {
            case ScopeKind.Department:
                BreadcrumbCategoryText.Text = "Global Dept.";
                BreadcrumbTargetText.Text = "All Subagents";
                break;
            case ScopeKind.Group:
                BreadcrumbCategoryText.Text = "Workspaces";
                BreadcrumbTargetText.Text = scope.DisplayName;
                break;
            case ScopeKind.Team:
                var parentGroup = _groups.FirstOrDefault(g => g.RootPath == scope.ParentGroupPath);
                BreadcrumbCategoryText.Text = parentGroup?.DisplayName ?? "Workspaces";
                BreadcrumbTargetText.Text = $"{scope.DisplayName} Team";
                break;
        }

        // グローバルルールボタンは常に有効
        ScopeRuleButton.IsEnabled = true;
        ScopeRuleButton.Opacity = 1.0;

        // Manage Team ボタンは Dashboard(Department)選択時は非活性、グループ/チーム選択時は活性
        var hasTeamContext = _currentScope.Kind != ScopeKind.Department;
        ManageTeamButton.IsEnabled = hasTeamContext;
        ManageTeamButton.Opacity = hasTeamContext ? 1.0 : 0.45;
        ManageTeamButton.Content = BuildRuleButtonContent(_currentScope);
        ManageTeamButton.ToolTip = hasTeamContext ? $"{_currentScope.DisplayName} のルールを管理" : "ワークスペースまたはチームを選択してください";

        ImportTemplateButton.ToolTip = _currentScope.Kind == ScopeKind.Department
            ? "共通 (グローバル) に職種別テンプレートからメンバーを一括追加"
            : $"{_currentScope.DisplayName} に職種別テンプレートからメンバーを一括追加";

        // 2. リーダーバナー ＆ メンバーカード描画(同名メンバーの警告判定を先に済ませてカードに反映する: BUG-18)
        RefreshDuplicateWarning();
        RefreshLeaderBanner();
        RefreshActiveLeaders();
        RefreshMemberCards();
        RefreshDashboardActivity(reloadEntries: true);
    }

    private void RefreshLeaderBanner()
    {
        // Git境界によるルール断絶警告 (U-5): 選択中チームが独立Gitリポジトリなら警告を表示
        var isIsolatedTeam = _currentScope.Kind == ScopeKind.Team &&
            _groups.SelectMany(g => g.Teams).Any(t => t.RootPath == _currentScope.FolderPath && t.IsIndependentGitRepo);
        GitIsolationInfoBox.Visibility = isIsolatedTeam ? Visibility.Visible : Visibility.Collapsed;

        // 1. スコープに応じたラベル・アイコン・表示名・説明の決定
        string scopeKindLabel;
        string scopeSubtitle;

        switch (_currentScope.Kind)
        {
            case ScopeKind.Department:
                scopeKindLabel = DashboardActivityCollector.BannerKindLabel;
                scopeSubtitle = DashboardActivityCollector.BannerSubtitle;
                break;
            case ScopeKind.Group:
                scopeKindLabel = "WORKSPACE";
                scopeSubtitle = _currentScope.FolderPath ?? "";
                break;
            case ScopeKind.Team:
            default:
                scopeKindLabel = "TEAM";
                scopeSubtitle = _currentScope.FolderPath ?? "";
                break;
        }

        BannerScopeKindText.Text = scopeKindLabel;
        LeaderBannerNameText.Text = _currentScope.DisplayName;
        LeaderBannerRoleText.Text = string.IsNullOrEmpty(scopeSubtitle) ? "Sloth Studio" : scopeSubtitle;

        // 2. 現在のスコープのエージェント一覧取得（専門メンバー数およびClaude/Gemini内訳）
        //    メンバー欄のカードと同じ範囲で数える(グループ選択時は直下＋配下の全チーム、BUG-24)
        var memberLists = new List<IReadOnlyList<AgentDefinition>>();
        foreach (var section in BuildMemberSections())
        {
            var sectionAgents = AgentDefinitionLoader.LoadScopeAgents(section.ClaudeDir, section.GeminiDir, section.ScopeType);
            RememberKnownMembers(sectionAgents);
            memberLists.Add(sectionAgents);
        }

        var metrics = ScopeMetrics.Calculate(memberLists, _activityMonitor.States, GetLeadersForCurrentScope());
        var totalCount = metrics.TotalMembers;

        MetricTotalMembersText.Text = totalCount.ToString();
        MetricTotalMembersSubText.Text = totalCount > 0
            ? $"専門メンバー (🦥{metrics.ClaudeMembers} / 🌟{metrics.GeminiMembers})"
            : "専門メンバー (未配備)";

        // 3. 作業中メンバー数 (エージェント + 稼働中リーダー。リーダーはAIごとに1人で数える)
        var runningAgents = metrics.RunningMembers;
        var activeLeadersCount = metrics.ActiveLeaders;
        var totalActive = metrics.TotalActive;

        MetricActiveAgentsText.Text = totalActive.ToString();
        MetricActiveAgentsSubText.Text = totalActive > 0
            ? $"作業中 ({runningAgents} メンバー + {activeLeadersCount} リーダー)"
            : "作業中 (Active)";

        // 4. ステータスバッジの更新
        if (totalActive > 0)
        {
            LeaderBannerStatusBadgeText.Text = $"{totalActive} 稼働中";
            LeaderBannerStatusBadgeBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#DBEAFE")!;
            LeaderBannerStatusBadgeText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#1D4ED8")!;
        }
        else
        {
            LeaderBannerStatusBadgeText.Text = totalCount > 0 ? "Ready" : "Empty";
            LeaderBannerStatusBadgeBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFromString(totalCount > 0 ? "#DCFCE7" : "#F1F5F9")!;
            LeaderBannerStatusBadgeText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(totalCount > 0 ? "#15803D" : "#64748B")!;
        }
    }

    /// <summary>「直近の会話」とみなす時間(Claude・Gemini とも分)を設定からモニターへ反映する。</summary>
    private void ApplyRecentConversationSettings()
    {
        _activityMonitor.ClaudeRecentIdleMinutes = _cardEffectSettings.EffectiveClaudeRecentConversationMinutes;
        _activityMonitor.GeminiRecentMinutes = _cardEffectSettings.EffectiveGeminiRecentConversationMinutes;
        _activityMonitor.AsyncSubagentMaxTrackMinutes = _cardEffectSettings.EffectiveAsyncSubagentMaxTrackMinutes;
        _activityMonitor.MinRunningDisplaySeconds = _cardEffectSettings.EffectiveMinRunningDisplaySeconds;
        DiagnosticLog.Shared.Enabled = _cardEffectSettings.DiagnosticLogEnabled;
        DiagnosticLog.Shared.MaxBytes = _cardEffectSettings.EffectiveDiagnosticLogMaxBytes;
    }

    /// <summary>
    /// 表示中の範囲に属する、待機中だが直近に会話があったClaudeセッションの数を返す(「会話N件」に足す用)。
    /// </summary>
    /// <returns>直近の待機会話の件数。</returns>
    private int CountRecentIdleClaudeSessionsForCurrentScope()
    {
        if (_currentScope.Kind == ScopeKind.Department)
        {
            var roots = _groups.Count == 0
                ? [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]
                : _groups.Select(g => g.RootPath).ToList();
            return roots.SelectMany(_activityMonitor.GetRecentIdleLeaderSessionIds).Distinct().Count();
        }

        var targetDir = _currentScope.FolderPath ?? (_groups.Count > 0 ? _groups[0].RootPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return _activityMonitor.GetRecentIdleLeaderSessionIds(targetDir).Count;
    }

    /// <summary>
    /// 表示中の範囲(全社/ワークスペース/チーム)に属するリーダーの会話(セッション)一覧を返す。
    /// 「作業中 (Active)」のリーダー数とリーダーカードは、どちらもこれをAIごとに1人(1枚)へまとめて使う(BUG-24)。
    /// </summary>
    /// <returns>会話の一覧(作業中または完了直後のもの)。</returns>
    private IReadOnlyList<LeaderSessionInfo> GetLeadersForCurrentScope()
    {
        if (_currentScope.Kind == ScopeKind.Department)
        {
            if (_groups.Count == 0)
            {
                return _activityMonitor.GetLeaders(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            }
            return _groups
                .SelectMany(g => _activityMonitor.GetLeaders(g.RootPath))
                .DistinctBy(l => (l.SessionId, l.Engine))
                .ToList();
        }

        var targetDir = _currentScope.FolderPath ?? (_groups.Count > 0 ? _groups[0].RootPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return _activityMonitor.GetLeaders(targetDir);
    }

    /// <summary>
    /// TEAM欄のリーダー(相棒)カードを最新状態に反映する。1つのAIにつき1枚で、CLIが使えるAIだけ出す
    /// (最近の会話が無くても待機表示で出す)。同じAIの会話が複数あれば、どれか作業中なら作業中・
    /// 「作業中:」はいちばん新しい会話の内容・2件以上なら「会話N件」を添える。
    /// 5秒間隔のポーリングで毎回呼ばれるため、既存のカードは作り直さずプロパティ更新のみ行う(チカチカ防止、U-22)。
    /// 作業中→完了を検知したら、メンバーカードと同じく「ぷるん」・光・(設定に従い)完了音とタスクバー点滅を行う。
    /// </summary>
    private void RefreshActiveLeaders()
    {
        // 表示範囲が変わったら集める会話も変わるため、完了検知をやり直す(範囲の切り替えで「完了」と誤検知しないように)
        var scopeKey = $"{_currentScope.Kind}|{_currentScope.FolderPath}";
        if (scopeKey != _leaderObservedScopeKey)
        {
            _leaderAttention = new MemberCardAttentionTracker();
            _leaderObservedScopeKey = scopeKey;
        }

        var sessions = GetLeadersForCurrentScope();
        var engines = LeaderCardSummary.GetVisibleEngines(_isClaudeCliAvailable, _isGeminiCliAvailable);
        var recentIdleClaudeCount = CountRecentIdleClaudeSessionsForCurrentScope();

        // CLIが使えなくなったAIのカードを外す
        foreach (var engine in _leaderCards.Keys.Where(e => !engines.Contains(e)).ToList())
        {
            LeaderCardsPanel.Children.Remove(_leaderCards[engine].Card);
            _leaderCards.Remove(engine);
            _leaderSummaries.Remove(engine);
        }

        var completed = new List<LeaderCardRefs>();
        for (var i = 0; i < engines.Count; i++)
        {
            var engine = engines[i];
            var summary = LeaderCardSummary.Summarize(sessions, engine, engine == AgentEngineKind.Claude ? recentIdleClaudeCount : 0);
            _leaderSummaries[engine] = summary;

            if (!_leaderCards.TryGetValue(engine, out var refs))
            {
                refs = CreateLeaderCard(engine);
                _leaderCards[engine] = refs;
            }

            // 表示順をClaude→Geminiに揃える
            var currentIndex = LeaderCardsPanel.Children.IndexOf(refs.Card);
            if (currentIndex != i)
            {
                if (currentIndex >= 0)
                {
                    LeaderCardsPanel.Children.Remove(refs.Card);
                }
                LeaderCardsPanel.Children.Insert(Math.Min(i, LeaderCardsPanel.Children.Count), refs.Card);
            }

            if (_leaderAttention.Observe(engine.ToString(), summary.State))
            {
                completed.Add(refs);
            }

            UpdateLeaderCard(refs, summary);
        }

        // カードの枚数が変わるとTEAM欄に収まるかどうかも変わるため、置き場所を決め直す
        UpdateTeamBannerLayout();

        if (completed.Count == 0)
        {
            return;
        }

        // 完了通知(メンバーと同じ設定に従う)。同じ周期で両方のAIが完了しても音と点滅は1回にまとめる
        var notifySettings = AppSettingsLoader.Load();
        if (notifySettings.AvatarBounceEnabled)
        {
            foreach (var refs in completed)
            {
                PlayAvatarBounce(refs.Card, notifySettings);
            }
        }

        if (notifySettings.CompletionSoundEnabled)
        {
            CompletionSound.Play(notifySettings.CompletionSoundPath);
        }

        if (notifySettings.TaskbarFlashEnabled)
        {
            TaskbarFlasher.FlashIfInactive(this);
        }
    }

    /// <summary>
    /// TEAM欄の本体の幅が変わったとき、リーダーカードを右に並べるか下の段へ移すかを決め直す。
    /// </summary>
    /// <param name="sender">TEAM欄の本体のGrid。</param>
    /// <param name="e">サイズ変更の情報(横幅が変わったときだけ決め直す)。</param>
    private void TeamBannerBodyGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            UpdateTeamBannerLayout();
        }
    }

    /// <summary>
    /// TEAM欄のリーダーカードの置き場所を決める。左のチーム情報の横に全部のカードが収まるなら右側に並べ、
    /// 収まらなければチーム情報の下の段へ移す(下の段では横幅いっぱいを使い、それでも足りなければカード同士も折り返す)。
    /// ウィンドウが狭いときにカードが見切れないようにするため。
    /// </summary>
    private void UpdateTeamBannerLayout()
    {
        // 中央寄せ・右寄せの判定はカード枚数でも変わるため、毎回計算し直す(状態は _leaderLayoutMode で保持)
        var available = TeamBannerBodyGrid.ActualWidth;
        if (available <= 0)
        {
            return;
        }

        // 左のチーム情報が折り返さずに並んだときの幅(チーム名・メトリクス・ボタンの自然な幅)
        TeamBannerInfoPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var infoWidth = TeamBannerInfoPanel.DesiredSize.Width;
        TeamBannerInfoPanel.InvalidateMeasure();

        var leadersWidth = LeaderCardsPanel.Children.Count * (LeaderCardWidth + LeaderCardSpacing);
        var sideBySide = leadersWidth <= 0 || infoWidth + leadersWidth <= available;

        // 横並びのとき、カード全体(先頭の左余白を除く)をパネルの水平中央に置いてもチーム情報と重ならないなら中央寄せ、重なるなら右寄せ
        var contentWidth = leadersWidth - LeaderCardSpacing;
        var centered = sideBySide && (available - contentWidth) / 2 >= infoWidth;

        // 0=中央寄せ(横並び) / 1=右寄せ(横並び) / 2=下の段
        var mode = !sideBySide ? 2 : (centered ? 0 : 1);
        if (_leaderLayoutMode == mode)
        {
            return;
        }

        _leaderLayoutMode = mode;
        Grid.SetRow(LeaderCardsPanel, sideBySide ? 0 : 1);
        Grid.SetColumn(LeaderCardsPanel, (sideBySide && !centered) ? 1 : 0);
        Grid.SetColumnSpan(LeaderCardsPanel, (sideBySide && !centered) ? 1 : 2);
        LeaderCardsPanel.HorizontalAlignment = !sideBySide
            ? HorizontalAlignment.Left
            : (centered ? HorizontalAlignment.Center : HorizontalAlignment.Right);

        // カードの左余白・下余白をパネル側で打ち消して端をそろえる(下の段ではチーム情報との間を少し空ける)
        LeaderCardsPanel.Margin = mode switch
        {
            0 => new Thickness(-LeaderCardSpacing, 0, 0, -LeaderCardRowSpacing),
            1 => new Thickness(0, 0, 0, -LeaderCardRowSpacing),
            _ => new Thickness(-LeaderCardSpacing, 16, 0, -LeaderCardRowSpacing),
        };
    }

    /// <summary>
    /// リーダー(相棒)カードを新規生成する。メンバーカードと同じ作りの縦長カード(約260×400)で、アバターをカード全面に敷き、
    /// 下部に重ねた黒っぽい文字エリア(左サイドバーと同色)の上にキャラ名(大きく)を中央寄せで重ねる(AI名・会話件数は画像左上)。
    /// 右上に目立たない「相棒」ラベルを置く。状態バッジ・揺れ・光はメンバーカードと同じ仕組み(Tag)で更新する。
    /// </summary>
    /// <param name="engine">カードのAI(Claude または Gemini)。</param>
    /// <returns>生成したカードと可変部分への参照。</returns>
    private LeaderCardRefs CreateLeaderCard(AgentEngineKind engine)
    {
        var card = new Border
        {
            Width = LeaderCardWidth,
            Height = LeaderCardHeight,
            // 揺れ(傾き)で下の角に出る数pxの隙間に白が見えるようにする(メンバーカードと同じ)
            Background = Brushes.White,
            BorderBrush = CardBorderDefault,
            BorderThickness = new Thickness(CardBorderThickness),
            CornerRadius = new CornerRadius(LeaderCardCornerRadius),
            Margin = new Thickness(LeaderCardSpacing, 0, 0, LeaderCardRowSpacing),
            // メンバーカードの一覧(Tag が AgentDefinition)と区別するため、Tag にはAIの種類を入れる
            Tag = engine,
        };

        // カードの角丸に合わせて中身(アバター・下地)を切り抜く。外側の光(Effect)はカード側に付くので切れない。
        var rootGrid = new Grid
        {
            Clip = new RectangleGeometry(new Rect(0, 0, LeaderCardWidth - CardBorderThickness * 2, LeaderCardHeight - CardBorderThickness * 2),
                LeaderCardCornerRadius - CardBorderThickness, LeaderCardCornerRadius - CardBorderThickness),
        };

        // 1. 背景: アバターをカード全面に(「ぷるん」「ゆらゆら」ではみ出した分は上の切り抜きで隠れる)
        rootGrid.Children.Add(CreateLeaderAvatarVisual(engine));

        // 3. 右上: 「相棒」ラベル(リーダーだと分かる程度に、小さく目立たない半透明のピル)
        rootGrid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            CornerRadius = new CornerRadius(LeaderBadgeCornerRadius),
            Padding = new Thickness(8, 1.5, 8, 1.5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, LeaderCardTopBadgeMarginTop, 10, 0),
            Child = new TextBlock
            {
                Text = "相棒",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#64748B")!,
            },
        });

        // 4. 下部: キャラ名(白系の文字・水平中央寄せ)
        // 画像の大きさ・位置は変えず、下部に左サイドバーと同じ黒っぽい色をベタ塗りした文字エリアを重ねる
        var contentStack = new StackPanel();
        var textArea = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            MinHeight = LeaderCardTextAreaMinHeight,
            // 画像が薄く透ける程度の半透明(画像はカード全面に敷いてあり、この帯の背後まで続く)
            Background = new SolidColorBrush(Color.FromArgb(
                LeaderCardTextAreaBackgroundAlpha,
                LeaderCardTextAreaBackgroundRgb.R, LeaderCardTextAreaBackgroundRgb.G, LeaderCardTextAreaBackgroundRgb.B)),
            Padding = new Thickness(16, 8, 16, 10),
            Child = contentStack,
        };

        // 4-1. キャラ名(大きく)
        var nameText = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // リーダーカードのみキャラ名を水平中央寄せ(メンバーカードは左寄せのまま)
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        };
        contentStack.Children.Add(nameText);

        // 4-2. 画像の左上に重ねる情報(AI名・状態バッジ・会話件数)。右上の「相棒」ラベルとは左右で離れている
        var overlayStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, LeaderCardTopBadgeMarginTop, 0, 0),
        };
        var overlayBackground = new SolidColorBrush(Color.FromArgb(LeaderCardOverlayBackgroundAlpha, 0, 0, 0));
        overlayStack.Children.Add(new Border
        {
            Background = overlayBackground,
            CornerRadius = new CornerRadius(LeaderBadgeCornerRadius),
            Padding = new Thickness(8, 1.5, 8, 1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = LeaderCardSummary.GetEngineLabel(engine),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
            },
        });
        // 状態バッジは表示しない(状態は下部の黒エリアの「待機中／作業中」テキストで分かるため)

        // 4-3. 会話が2件以上のときだけ「会話N件」を半透明の黒い角丸背景で添える(件数が無いときは背景ごと隠す)
        var sessionCountText = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.White,
            Visibility = Visibility.Collapsed,
        };
        var sessionCountPill = new Border
        {
            Background = overlayBackground,
            CornerRadius = new CornerRadius(LeaderBadgeCornerRadius),
            Padding = new Thickness(8, 1.5, 8, 1.5),
            // AI名バッジ(左上)・「相棒」ラベル(右上)と同じ上マージンで、カード上部の水平中央に置く(1行に見せる)
            Margin = new Thickness(0, LeaderCardTopBadgeMarginTop, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = sessionCountText,
        };
        sessionCountPill.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = sessionCountText });

        rootGrid.Children.Add(overlayStack);
        rootGrid.Children.Add(sessionCountPill);
        rootGrid.Children.Add(textArea);

        // 5. 完了後の光る枠(普段は非表示。メンバーカードと同じく中身の上に重ね、当たり判定は無し)
        rootGrid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(LeaderCardCornerRadius - CardBorderThickness),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Tag = GlowFrameTag,
        });

        card.Child = rootGrid;

        var refs = new LeaderCardRefs
        {
            Card = card,
            Engine = engine,
            NameText = nameText,
            SessionCountText = sessionCountText,
        };

        // 画面に載ったら今の状態で揺れ・光・バッジを復元し、外れたらアニメーションを止める(メンバーカードと同じ)
        card.Loaded += (_, _) =>
        {
            if (_leaderSummaries.TryGetValue(engine, out var summary))
            {
                ApplyLeaderCardPresentation(refs, summary.State);
            }
        };
        card.Unloaded += (_, _) => SuspendCardEffects(card);

        // ホバー時は枠線の色だけ変える(メンバーカードと同じ。光っている間は光の色を上書きしない)
        card.MouseEnter += (_, _) =>
        {
            if (!CardGlowEffect.IsShown(card))
            {
                card.BorderBrush = CardBorderHoverAccent;
            }
        };
        card.MouseLeave += (_, _) =>
        {
            if (!CardGlowEffect.IsShown(card))
            {
                card.BorderBrush = CardBorderDefault;
            }
        };

        // クリックで他の画面は開かない。光っていれば「確認した」扱いにして光を消すだけ
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!_leaderAttention.Acknowledge(engine.ToString()))
            {
                return;
            }

            e.Handled = true;
            if (_leaderSummaries.TryGetValue(engine, out var summary))
            {
                ApplyLeaderCardPresentation(refs, summary.State);
            }
        };

        return refs;
    }

    /// <summary>
    /// リーダーカード全面に敷くアバター要素を作る。同梱画像(Assets\Avatars\leader-*.jpg)があれば顔が見えるよう上寄せで敷き詰め、
    /// 無ければAIごとの色の背景に従来のアイコン(🦥/🌟)を上寄りに出す。画像・アイコンが「ぷるん」「ゆらゆら」の対象(AvatarVisualTag)。
    /// </summary>
    /// <param name="engine">カードのAI(Claude または Gemini)。</param>
    /// <returns>アバター要素(アイコン表示のときは背景込みの入れ物)。</returns>
    private static FrameworkElement CreateLeaderAvatarVisual(AgentEngineKind engine)
    {
        var path = Path.Combine(AgentDefinitionLoader.DefaultBundledAvatarDirectory, LeaderCardSummary.GetAvatarFileName(engine));
        if (File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                // Image の UniformToFill は中央基準で切り取られるため、上寄せにできる ImageBrush で敷き詰める
                var imageVisual = new Border
                {
                    Background = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Top,
                    },
                    Tag = AvatarVisualTag,
                };
                RenderOptions.SetBitmapScalingMode(imageVisual, BitmapScalingMode.HighQuality);
                return imageVisual;
            }
            catch
            {
                // 画像が読めなければアイコン表示にフォールバックする
            }
        }

        // 画像が無いとき: AIごとの色の背景＋アイコン(下部の白っぽい下地に沈まないよう上寄りに置く)
        var fallback = new Grid
        {
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString(engine == AgentEngineKind.Gemini ? "#818CF8" : "#4F46E5")!,
        };
        var icon = CreateInitialVisual(engine == AgentEngineKind.Gemini ? "🌟" : "🦥", LeaderCardHeight);
        icon.Foreground = Brushes.White;
        fallback.Children.Add(icon);
        return fallback;
    }

    /// <summary>
    /// 既存のリーダーカードの可変部分(キャラ名・会話件数)と、状態バッジ・揺れ・光を更新する。
    /// </summary>
    /// <param name="refs">更新するカード。</param>
    /// <param name="summary">AIごとにまとめた会話の内容。</param>
    private void UpdateLeaderCard(LeaderCardRefs refs, LeaderCardSummary summary)
    {
        var name = LeaderCardSummary.GetCharacterName(_cardEffectSettings, refs.Engine);
        if (refs.NameText.Text != name)
        {
            refs.NameText.Text = name;
        }

        var countText = summary.BuildSessionCountText();
        refs.SessionCountText.Text = countText ?? string.Empty;
        refs.SessionCountText.Visibility = countText is null ? Visibility.Collapsed : Visibility.Visible;

        ApplyLeaderCardPresentation(refs, summary.State);
    }

    /// <summary>
    /// リーダーカードの状態バッジ・作業中の揺れ・完了後の光を、指定の状態に合わせる(メンバーカードと同じ演出)。
    /// 何度呼んでも同じ結果になる(すでに揺れている・光っているカードはそのまま続ける)。
    /// </summary>
    /// <param name="refs">対象のカード。</param>
    /// <param name="state">AIごとにまとめた状態。</param>
    private void ApplyLeaderCardPresentation(LeaderCardRefs refs, MemberActivityState state)
    {
        var card = refs.Card;
        UpdateStateBadge(card, MemberCardAttentionTracker.GetBadgeKind(state));

        var avatar = FindChild<FrameworkElement>(card, AvatarVisualTag);
        if (avatar is not null)
        {
            if (MemberCardAttentionTracker.ShouldSway(state, _cardEffectSettings.RunningSwayEnabled))
            {
                AvatarSwayAnimation.Start(
                    avatar,
                    _cardEffectSettings.EffectiveRunningSwayAngleDegrees,
                    _cardEffectSettings.EffectiveRunningSwayPeriodMilliseconds,
                    _cardEffectSettings.EffectiveRunningSwayBreathAmplitude,
                    LeaderCardHeight / LeaderCardWidth);
            }
            else
            {
                AvatarSwayAnimation.Stop(avatar, smooth: true);
            }
        }

        if (_leaderAttention.IsAwaitingAcknowledgement(refs.Engine.ToString()))
        {
            ShowCompletionGlow(card);
        }
        else
        {
            HideCompletionGlow(card);
        }
    }

    private void AddNotification(string title, string message)
    {
        _notifications.Insert(0, new StudioNotification(title, message, DateTime.Now));
        NotificationBadgeBorder.Visibility = Visibility.Visible;
    }

    private void NotificationBell_Click(object sender, MouseButtonEventArgs e)
    {
        NotificationBadgeBorder.Visibility = Visibility.Collapsed;

        var menu = new ContextMenu();
        if (_notifications.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "（新しい通知はありません）", IsEnabled = false });
        }
        else
        {
            var headerItem = new MenuItem { Header = "🔔 最近のアクティビティ通知", IsEnabled = false, FontWeight = FontWeights.Bold };
            menu.Items.Add(headerItem);
            menu.Items.Add(new Separator());

            foreach (var n in _notifications.Take(10))
            {
                var item = new MenuItem
                {
                    Header = $"{n.Title}\n{n.Message} ({n.Timestamp:HH:mm:ss})",
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var clearItem = new MenuItem { Header = "🗑️ 通知をすべて消去" };
            clearItem.Click += (_, _) => _notifications.Clear();
            menu.Items.Add(clearItem);
        }

        ApplyRoundedMenuStyle(menu);
        menu.PlacementTarget = NotificationBellBorder;
        menu.IsOpen = true;
    }

    private (string claudeDir, string geminiSkillsDir, AgentScope scopeType) GetCurrentScopeAgentDirs()
    {
        return _currentScope.Kind switch
        {
            ScopeKind.Department => (GlobalAgentsDir, GlobalGeminiSkillsDir, AgentScope.Global),
            ScopeKind.Group => (
                Path.Combine(_currentScope.FolderPath!, ".claude", "agents"),
                Path.Combine(_currentScope.FolderPath!, ".agents", "skills"),
                AgentScope.Group),
            ScopeKind.Team => (
                Path.Combine(_currentScope.FolderPath!, ".claude", "agents"),
                Path.Combine(_currentScope.FolderPath!, ".agents", "skills"),
                AgentScope.Team),
            _ => (GlobalAgentsDir, GlobalGeminiSkillsDir, AgentScope.Global),
        };
    }

    /// <summary>
    /// 所属先の.claude/agentsフォルダから、そのメンバー一覧を読むための3点(Claude側・Gemini側フォルダ・範囲の種類)を求める。
    /// 全社共通ならグローバルのフォルダ、登録済みワークスペース直下ならグループ、それ以外はチームとして扱う。
    /// </summary>
    /// <param name="claudeDir">所属先の.claude/agentsフォルダの絶対パス。</param>
    /// <returns>Claude側フォルダ・Gemini側フォルダ・範囲の種類。</returns>
    private (string claudeDir, string geminiSkillsDir, AgentScope scopeType) ResolveScopeDirs(string claudeDir)
    {
        if (string.Equals(Path.GetFullPath(claudeDir), Path.GetFullPath(GlobalAgentsDir), StringComparison.OrdinalIgnoreCase))
        {
            return (GlobalAgentsDir, GlobalGeminiSkillsDir, AgentScope.Global);
        }

        // .claude/agents の2つ上が所属先のルートフォルダ
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(claudeDir)))!;
        var isGroup = _groups.Any(g => string.Equals(Path.GetFullPath(g.RootPath), root, StringComparison.OrdinalIgnoreCase));
        return (claudeDir, Path.Combine(root, ".agents", "skills"), isGroup ? AgentScope.Group : AgentScope.Team);
    }

    private void EngineFilter_Click(object sender, RoutedEventArgs e)
    {
        if (FilterClaudeRadio.IsChecked == true)
        {
            _engineFilter = "Claude";
        }
        else if (FilterGeminiRadio.IsChecked == true)
        {
            _engineFilter = "Gemini";
        }
        else
        {
            _engineFilter = "All";
        }

        RefreshMemberCards();
    }

    private const string AddMemberCardKey = "__add_member__";

    /// <summary>同名警告バッジ付きで構築したカードに付ける目印(Uid)。警告状態が変わった時にカードを作り直す判定に使う。</summary>
    private const string DuplicateCardUid = "duplicate-warning";
    private const string DirectSectionId = "__direct__";

    /// <summary>
    /// 現在のスコープに応じて表示すべきメンバーセクションの一覧を組み立てる(U-21)。
    /// グループ(ワークスペース)選択時は、グループ直下の専門メンバーに加えて配下の全チームの
    /// メンバーもチーム名の見出し付きでまとめて表示する。それ以外(全社/チーム選択時)は
    /// 見出し無しの単一セクションのみを返す。
    /// </summary>
    private List<MemberSection> BuildMemberSections()
    {
        if (_currentScope.Kind == ScopeKind.Group)
        {
            var group = _groups.FirstOrDefault(g => g.RootPath == _currentScope.FolderPath);

            var sections = new List<MemberSection>
            {
                new()
                {
                    SectionId = DirectSectionId,
                    Header = group is { Teams.Count: > 0 } ? $"🏠 {_currentScope.DisplayName} 直下" : null,
                    ClaudeDir = Path.Combine(_currentScope.FolderPath!, ".claude", "agents"),
                    GeminiDir = Path.Combine(_currentScope.FolderPath!, ".agents", "skills"),
                    ScopeType = AgentScope.Group,
                    OwnerLabel = _currentScope.DisplayName,
                    ShowAddMemberCard = true,
                },
            };

            if (group is not null)
            {
                foreach (var team in group.Teams)
                {
                    sections.Add(new MemberSection
                    {
                        SectionId = team.RootPath,
                        Header = $"👥 {team.DisplayName} チーム",
                        ClaudeDir = Path.Combine(team.RootPath, ".claude", "agents"),
                        GeminiDir = Path.Combine(team.RootPath, ".agents", "skills"),
                        ScopeType = AgentScope.Team,
                        OwnerLabel = team.DisplayName,
                        ShowAddMemberCard = false,
                    });
                }
            }

            return sections;
        }

        var (claudeDir, geminiDir, scopeType) = GetCurrentScopeAgentDirs();
        return
        [
            new MemberSection
            {
                SectionId = DirectSectionId,
                Header = null,
                ClaudeDir = claudeDir,
                GeminiDir = geminiDir,
                ScopeType = scopeType,
                OwnerLabel = _currentScope.DisplayName,
                ShowAddMemberCard = true,
            },
        ];
    }

    /// <summary>検索・エンジンフィルタを適用し、名前順にソートする(セクション共通処理)。</summary>
    private List<AgentDefinition> FilterAndSortAgents(IReadOnlyList<AgentDefinition> agents)
    {
        // 検索フィルタ適用 (表示名・説明・識別子・ツール群・モデル)
        var filtered = string.IsNullOrWhiteSpace(_searchFilter)
            ? agents.AsEnumerable()
            : agents.Where(a =>
                a.EffectiveDisplayName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                (a.Tools != null && a.Tools.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(a.Model) && a.Model.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)));

        // エンジンフィルタ適用
        filtered = _engineFilter switch
        {
            "Claude" => filtered.Where(a => a.Engine is AgentEngineKind.Claude or AgentEngineKind.Shared),
            "Gemini" => filtered.Where(a => a.Engine is AgentEngineKind.Gemini or AgentEngineKind.Shared),
            _ => filtered,
        };

        // 名前順にソート
        return filtered
            .OrderBy(a => a.EffectiveDisplayName)
            .ToList();
    }

    /// <summary>セクション1つ分の外枠(見出し＋カード用WrapPanel)を新規生成する(U-21)。</summary>
    private static SectionContainer BuildSectionContainer(MemberSection section)
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        TextBlock? headerText = null;

        if (section.Header is not null)
        {
            headerText = new TextBlock
            {
                Text = section.Header,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#334155")!,
                Margin = new Thickness(2, 0, 0, 10),
            };
            root.Children.Add(headerText);
        }

        var cardsPanel = new WrapPanel();
        root.Children.Add(cardsPanel);

        return new SectionContainer { Root = root, HeaderText = headerText, CardsPanel = cardsPanel };
    }

    /// <summary>
    /// メンバーカード一覧を最新状態に反映する。ポーリングの度に全カードを作り直すと
    /// 画面がチカチカする（U-22）ため、既存の識別子（エージェント名）が一致するカードは
    /// 再利用し、並び替えだけを行う。
    /// U-21: グループ選択時は配下チームごとにセクション分けして表示するため、
    /// セクション単位で同様の差分更新を行う。
    /// </summary>
    private void RefreshMemberCards()
    {
        var sections = BuildMemberSections();

        var totalAgentCount = 0;
        var totalFilteredCount = 0;

        // 現在のセクション構成に合わないセクションを除去(例: チーム削除、スコープ切替)。
        var currentSectionIds = sections.Select(s => s.SectionId).ToHashSet();
        foreach (var staleId in _memberSections.Keys.Where(id => !currentSectionIds.Contains(id)).ToList())
        {
            MembersGridPanel.Children.Remove(_memberSections[staleId].Root);
            _memberSections.Remove(staleId);
            _memberCardsBySection.Remove(staleId);
        }

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var agents = AgentDefinitionLoader.LoadScopeAgents(section.ClaudeDir, section.GeminiDir, section.ScopeType);
            RememberKnownMembers(agents);
            var sortedList = FilterAndSortAgents(agents);

            totalAgentCount += agents.Count;
            totalFilteredCount += sortedList.Count;

            if (!_memberSections.TryGetValue(section.SectionId, out var container))
            {
                container = BuildSectionContainer(section);
                _memberSections[section.SectionId] = container;
                _memberCardsBySection[section.SectionId] = [];
                MembersGridPanel.Children.Add(container.Root);
            }
            else if (container.HeaderText is not null)
            {
                container.HeaderText.Text = section.Header ?? string.Empty;
            }

            var sectionCurrentIndex = MembersGridPanel.Children.IndexOf(container.Root);
            if (sectionCurrentIndex != sectionIndex)
            {
                MembersGridPanel.Children.Remove(container.Root);
                MembersGridPanel.Children.Insert(sectionIndex, container.Root);
            }

            var cardsDict = _memberCardsBySection[section.SectionId];

            // いなくなったエージェントのカードを除去
            var currentKeys = sortedList.Select(a => a.Name).ToHashSet();
            if (section.ShowAddMemberCard)
            {
                currentKeys.Add(AddMemberCardKey);
            }
            foreach (var staleKey in cardsDict.Keys.Where(k => !currentKeys.Contains(k)).ToList())
            {
                container.CardsPanel.Children.Remove(cardsDict[staleKey]);
                cardsDict.Remove(staleKey);
            }

            // 既存カードは再利用、新規はカードを生成。並び順もソート結果に揃える。
            for (var i = 0; i < sortedList.Count; i++)
            {
                var agent = sortedList[i];

                // BUG-17: 既存カードの内容(ディスク上のファイル定義)が外部編集で変わっていた場合、
                // 再利用だけでは反映されない。キャッシュ済みAgentDefinition(record)と
                // 値が完全一致する場合のみ差分更新に留め、変わっていればカードを作り直す。
                if (!cardsDict.TryGetValue(agent.Name, out var card) ||
                    card.Tag is not AgentDefinition cachedAgent ||
                    cachedAgent != agent ||
                    (card.Uid == DuplicateCardUid) != _duplicateFilePaths.Contains(agent.FilePath))
                {
                    if (card is not null)
                    {
                        container.CardsPanel.Children.Remove(card);
                    }
                    card = BuildVerticalStudioCard(agent, section.OwnerLabel);
                    cardsDict[agent.Name] = card;
                    container.CardsPanel.Children.Add(card);
                }

                var currentIndex = container.CardsPanel.Children.IndexOf(card);
                if (currentIndex != i)
                {
                    container.CardsPanel.Children.Remove(card);
                    container.CardsPanel.Children.Insert(i, card);
                }
            }

            if (section.ShowAddMemberCard)
            {
                // 末尾に縦長の「＋ Add New Member」カードを配置！
                // スコープ（追加先ディレクトリ）が変わった場合は古いカードを作り直す。
                if (cardsDict.TryGetValue(AddMemberCardKey, out var existingAddCard) && existingAddCard.Tag as string != section.ClaudeDir)
                {
                    container.CardsPanel.Children.Remove(existingAddCard);
                    cardsDict.Remove(AddMemberCardKey);
                }

                if (!cardsDict.TryGetValue(AddMemberCardKey, out var addCard))
                {
                    addCard = BuildAddMemberCard(_currentScope, section.ClaudeDir);
                    addCard.Tag = section.ClaudeDir;
                    cardsDict[AddMemberCardKey] = addCard;
                }

                if (container.CardsPanel.Children.Contains(addCard))
                {
                    container.CardsPanel.Children.Remove(addCard);
                }
                container.CardsPanel.Children.Add(addCard);
            }
        }

        // 初回起動時ウェルカム案内(Empty State Panel): ワークスペースが1件も登録されておらず、
        // 全社スコープでメンバーも0人の「まっさらな初見ユーザー」の時だけ表示する。
        var showWelcomePanel = _groups.Count == 0 && _currentScope.Kind == ScopeKind.Department && totalAgentCount == 0;
        WelcomeEmptyStatePanel.Visibility = showWelcomePanel ? Visibility.Visible : Visibility.Collapsed;

        EmptyStateBorder.Visibility = showWelcomePanel
            ? Visibility.Collapsed
            : totalFilteredCount == 0 && (!string.IsNullOrWhiteSpace(_searchFilter) || _engineFilter != "All")
                ? Visibility.Visible
                : (totalAgentCount == 0 ? Visibility.Visible : Visibility.Collapsed);
    }

    // ---- A案縦長スタジオカードの構築 ----

    /// <summary>Claudeモデル値から一言特徴を返す(U-23)。未設定時はsonnet相当を既定とする。</summary>
    private static string GetClaudeModelTrait(string? model) => model?.Trim().ToLowerInvariant() switch
    {
        "haiku" => "haiku: ⚡ 高速・軽量（単純作業向け）",
        "opus" => "opus: 🧠 熟考・高精度（難しい判断向け）",
        "inherit" => "inherit: 🔄 継承（呼び出し元と同じ）",
        _ => "sonnet: 🎯 標準バランス（手堅く丁寧）",
    };

    /// <summary>Geminiモデル値から一言特徴を返す(U-23)。未設定時はflash相当(スタジオ標準)を既定とする。</summary>
    private static string GetGeminiModelTrait(string? model) => model?.Trim().ToLowerInvariant() switch
    {
        "flash_lite" => "flash_lite: ⚡ 超軽量・最速（分類向け）",
        "pro" => "pro: 🧠 最高知能（複雑な設計向け）",
        "inherit" => "inherit: 🔄 継承（呼び出し元と同じ）",
        _ => "flash: 🚀 高速・低コスト（探索・調査向け）",
    };

    /// <summary>カードホバー時のアクセントボーダー色(トレカ風デザイン全体で固定使用)。</summary>
    private static readonly SolidColorBrush CardBorderHoverAccent = new((Color)ColorConverter.ConvertFromString("#4F46E5")!);

    /// <summary>
    /// アバター画像が無いカード背景に敷くスレート系グラデーション。
    /// カード下部が明るい下地(白っぽいグラデーション)になったため、暗すぎると上下で濁って見える。
    /// 中間の明るさのスレートにして、下地へ自然につながるようにした(2026-10-03)。
    /// </summary>
    /// <returns>Freeze済みのグラデーション。</returns>
    private static LinearGradientBrush CreateFallbackCardBackground() => AvatarInitialStyle.CreateBackground();

    /// <summary>メンバーカードの黒エリア上で「未接続」を示す明るい赤の文字色(暗い背景で読めるようにする)。</summary>
    private static readonly SolidColorBrush UnavailableTextBrushOnDark = new((Color)ColorConverter.ConvertFromString("#FCA5A5")!);

    /// <summary>
    /// 頭文字表示の上端の位置(カードの高さに対する割合)。下部の白っぽい下地(位置0.35から濃くなる)に
    /// 文字が沈まないよう、カードの上寄りに置く。見た目の調整用の固定値。
    /// </summary>
    private const double InitialTopRatio = 0.10;

    /// <summary>
    /// アバター画像が無いカードに出す頭文字(大きな半透明の白文字)を作る。「ぷるん」「ゆらゆら」の対象(AvatarVisualTag)。
    /// </summary>
    /// <param name="text">表示する文字(頭文字やアイコン)。</param>
    /// <param name="cardHeight">カードの高さ(上寄りの位置を決めるため)。</param>
    /// <returns>頭文字の要素。</returns>
    private static TextBlock CreateInitialVisual(string text, double cardHeight) => new()
    {
        Text = text,
        FontSize = 96,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, cardHeight * InitialTopRatio, 0, 0),
        Tag = AvatarVisualTag,
    };

    /// <summary>
    /// メンバーカードのマウスオーバー用ToolTipを作る。役割名(太字)・説明文・モデル情報・警告をまとめて1つのポップアップに出す。
    /// </summary>
    /// <param name="agent">対象のメンバー(説明文を使う)。</param>
    /// <param name="roleName">カードに表示している役割名。</param>
    /// <param name="modelLines">モデル情報の行(例: 「🦥 sonnet: …」)。</param>
    /// <param name="warningLines">警告の行(未接続・同名)。無ければ空。</param>
    /// <returns>表示用のToolTip。</returns>
    private static ToolTip CreateMemberCardToolTip(AgentDefinition agent, string roleName, IReadOnlyList<string> modelLines, IReadOnlyList<string> warningLines)
    {
        var panel = new StackPanel { MaxWidth = MemberCardToolTipMaxWidth };
        panel.Children.Add(new TextBlock
        {
            Text = roleName,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(agent.Description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = agent.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        foreach (var line in modelLines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        foreach (var line in warningLines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(MemberCardToolTipWarningColor)!),
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        return new ToolTip { Content = panel };
    }

    /// <summary>
    /// カードにToolTipを付け、マウスの動きに追従させる。WPF標準のToolTipは表示時の位置で固定されるため、
    /// カード上のMouseMoveでオフセットを更新して動かす。カードの外に出れば標準動作で閉じる(表示遅延も既定のまま)。
    /// 画面端でのはみ出しは、Relative配置のPopupが自動で画面内に収める。
    /// </summary>
    /// <param name="card">ToolTipを付けるカード。</param>
    /// <param name="toolTip">表示するToolTip。</param>
    private static void AttachFollowingToolTip(FrameworkElement card, ToolTip toolTip)
    {
        toolTip.Placement = PlacementMode.Relative;
        toolTip.PlacementTarget = card;
        toolTip.HorizontalOffset = MemberCardToolTipOffsetX;
        toolTip.VerticalOffset = MemberCardToolTipOffsetY;
        card.ToolTip = toolTip;
        card.MouseMove += (_, e) =>
        {
            var pos = e.GetPosition(card);
            toolTip.HorizontalOffset = pos.X + MemberCardToolTipOffsetX;
            toolTip.VerticalOffset = pos.Y + MemberCardToolTipOffsetY;
        };
    }
    /// <summary>
    /// フルブリード・トレカ風メンバーカードの構築。アバター画像がカード全面に敷かれ、
    /// 下部の半透明の黒エリア(リーダーカードと同じ定数)上に白系の文字で名前・ロール・モデル行を重ねる(状態バッジは廃止)。説明文はマウスオーバー(ToolTip)で表示する。
    /// </summary>
    /// <param name="agent">カードに表示するメンバー。</param>
    /// <param name="ownerLabel">メンバーの所属の表示名(詳細画面・メニューに渡す)。</param>
    /// <param name="teamLabel">黒帯内に小さく出す所属チーム名。nullなら出さない(Dashboard上部のカードだけが指定する)。</param>
    /// <param name="sourceClaudeDir">
    /// メンバーの所属先の.claude/agentsフォルダ。詳細画面を開くときに、表示中の画面ではなくこの所属先を基準にする
    /// (Dashboard上部は他チームのメンバーも並ぶため)。nullなら表示中の画面の範囲を使う。
    /// </param>
    /// <returns>生成したカード。</returns>
    private Border BuildVerticalStudioCard(AgentDefinition agent, string ownerLabel, string? teamLabel = null, string? sourceClaudeDir = null)
    {
        // 下部の文字エリアはリーダーカードと同じ半透明の黒。文字は白系(名前=白、その他=LeaderCardSubTextColor)
        var subTextBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(LeaderCardSubTextColor)!;

        // カード全体のコンテナ
        var card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            // 揺れ(傾き)でカード下の角に出る数pxの隙間に、ページの背景色ではなく白が見えるようにする
            // (その上に白っぽい下地が重なるので、隙間がほぼ見えなくなる)
            Background = Brushes.White,
            BorderBrush = CardBorderDefault,
            BorderThickness = new Thickness(CardBorderThickness),
            CornerRadius = new CornerRadius(CardCornerRadius),
            Margin = new Thickness(0, 0, CardGapHorizontal, CardGapVertical),
            Cursor = Cursors.Hand,
            Tag = agent,
            ClipToBounds = true,
        };

        // カードの角丸に合わせて中身(アバター・下の黒帯・光る枠)を切り抜く。Borderの ClipToBounds は四角の切り抜きなので、
        // 角丸に沿わせるためここで丸い切り抜きを指定する(黒帯の下端が角丸にはみ出さない)。外側の光(Effect)はカード側に付くので切れない。
        var rootGrid = new Grid
        {
            Clip = new RectangleGeometry(
                new Rect(0, 0, CardWidth - CardBorderThickness * 2, CardHeight - CardBorderThickness * 2),
                CardCornerRadius - CardBorderThickness, CardCornerRadius - CardBorderThickness),
        };

        // 利用不可エンジンの判定 (U-24): CLI未導入・ログイン切れ等でカードを非活性グレーアウト表示する
        var isClaudeUnavailable = agent.Engine is AgentEngineKind.Claude or AgentEngineKind.Shared && !_isClaudeCliAvailable;
        var isGeminiUnavailable = agent.Engine is AgentEngineKind.Gemini or AgentEngineKind.Shared && !_isGeminiCliAvailable;

        // Shared以外(単独エンジン)が利用不可な場合はカード全体をグレーアウトする
        if (agent.Engine != AgentEngineKind.Shared && (isClaudeUnavailable || isGeminiUnavailable))
        {
            card.Opacity = 0.55;
        }

        // 1. 背景レイヤー: アバター画像があればフルブリード表示、無ければスレート系グラデーション＋イニシャル
        ImageSource? avatarImage = null;
        if (!string.IsNullOrWhiteSpace(agent.AvatarPath) && File.Exists(agent.AvatarPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(agent.AvatarPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                avatarImage = bitmap;
            }
            catch
            {
                avatarImage = null;
            }
        }

        if (avatarImage is not null)
        {
            var avatarImageElement = new Image
            {
                Source = avatarImage,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = AvatarVisualTag,
            };
            RenderOptions.SetBitmapScalingMode(avatarImageElement, BitmapScalingMode.HighQuality);
            rootGrid.Children.Add(avatarImageElement);
        }
        else
        {
            var fallbackBg = new Border { Background = CreateFallbackCardBackground() };
            rootGrid.Children.Add(fallbackBg);

            // 頭文字は下部の白っぽい下地に半分沈まないよう、カードの上寄り(下地が透明な範囲)に置く
            rootGrid.Children.Add(CreateInitialVisual(GetInitial(agent.EffectiveDisplayName), CardHeight));
        }

        // 2. 下部の文字エリア(半透明の黒)は、後段(5.)で文字と一緒に重ねる

        // 3. 左上 エンジン識別バッジ (🦥 Claude / 🌟 Gemini / 🔄 Shared)
        var enginePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var engineText = new TextBlock
        {
            Text = agent.Engine switch
            {
                AgentEngineKind.Gemini => "🌟 Gemini",
                // Shared は両エンジンの名前をアイコン付きで1行表示する(アイコンだけでは初見で分からないため)
                AgentEngineKind.Shared => MemberSharedBadgeText,
                _ => "🦥 Claude",
            },
            FontSize = agent.Engine == AgentEngineKind.Shared ? MemberCardSharedBadgeFontSize : MemberCardBadgeFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#0F172A")!,
        };
        enginePanel.Children.Add(engineText);

        if (agent.Engine != AgentEngineKind.Shared && (isClaudeUnavailable || isGeminiUnavailable))
        {
            enginePanel.Children.Add(new TextBlock
            {
                Text = " ⚠️未接続",
                FontSize = MemberCardBadgeFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#DC2626")!,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // 同名メンバー警告(BUG-18): 既存データで重複しているメンバーには⚠️バッジを出し、リネームを促す
        if (_duplicateFilePaths.Contains(agent.FilePath))
        {
            card.Uid = DuplicateCardUid;
            enginePanel.Children.Add(new TextBlock
            {
                Text = " ⚠️同名",
                FontSize = MemberCardBadgeFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#DC2626")!,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }


        var engineBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
            CornerRadius = new CornerRadius(MemberEngineBadgeCornerRadius),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(MemberCardBadgeMargin, MemberCardBadgeMargin, 0, 0),
            Child = enginePanel,
        };
        // エンジン種別(Claude / Gemini / Shared)ごとの配備先ツールチップ
        engineBadge.ToolTip = MemberBadgeToolTip.For(agent.Engine);
        rootGrid.Children.Add(engineBadge);

        // 4. 右上 3点メニュー(画像の上に浮かぶ半透明ピル)
        var topRightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, MemberCardBadgeMargin, MemberCardBadgeMargin, 0),
        };

        // 状態バッジ(Idle/Active等)は表示しない。状態は作業中の揺れ・枠の光などの演出で分かる

        var menuBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
            CornerRadius = new CornerRadius(CardBadgeCornerRadius),
            Width = 24,
            Height = 24,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "⋮",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#334155")!,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            },
        };
        menuBadge.MouseLeftButtonUp += (s, e) =>
        {
            e.Handled = true;
            ShowCardContextMenu(agent, menuBadge, ownerLabel);
        };
        topRightPanel.Children.Add(menuBadge);

        rootGrid.Children.Add(topRightPanel);

        // 5. 下部コンテンツ(白系の文字): 名前・ロール・モデル行。リーダーカードと同じ半透明の黒エリアに載せる。
        //    エリアの高さは内容に合わせる。説明文は黒エリアに出さず、カード全体のToolTip(下記)で見せる。
        var contentStack = new StackPanel();
        var textArea = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(
                LeaderCardTextAreaBackgroundAlpha,
                LeaderCardTextAreaBackgroundRgb.R, LeaderCardTextAreaBackgroundRgb.G, LeaderCardTextAreaBackgroundRgb.B)),
            Padding = new Thickness(MemberCardTextAreaPaddingH, 6, MemberCardTextAreaPaddingH, 8),
            Child = contentStack,
        };

        var nameText = new TextBlock
        {
            Text = agent.EffectiveDisplayName,
            FontSize = MemberCardNameFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 1),
        };
        contentStack.Children.Add(nameText);

        var roleText = new TextBlock
        {
            Text = agent.DisplayName is not null ? agent.Name : "Subagent",
            FontSize = MemberCardRoleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = subTextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        };
        contentStack.Children.Add(roleText);

        // Dashboard上部のカードだけ、どのチームのメンバーか分かるよう所属チーム名を小さく添える
        if (!string.IsNullOrWhiteSpace(teamLabel))
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = MemberCardTeamLabelPrefix + teamLabel,
                FontSize = MemberCardTeamLabelFontSize,
                Foreground = subTextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        // モデル情報(Shared時の sonnet/flash 等)は黒エリアに出さず、ポップアップ(ToolTip)に載せる。
        // 黒エリアは名前・ロール行のみにして高さを詰める。
        roleText.Margin = new Thickness(0, 0, 0, 0);

        // 未接続・同名の警告もポップアップへ統合する(バッジ側の個別ToolTipは廃止して競合を防ぐ)
        var modelLines = new List<string>();
        var warningLines = new List<string>();
        if (agent.Engine is AgentEngineKind.Claude or AgentEngineKind.Shared)
        {
            modelLines.Add($"🦥 {GetClaudeModelTrait(agent.Model)}");
            if (isClaudeUnavailable) warningLines.Add("⚠️ Claude CLIが見つかりません。Settings画面でパスを確認してください。");
        }
        if (agent.Engine is AgentEngineKind.Gemini or AgentEngineKind.Shared)
        {
            modelLines.Add($"🌟 {GetGeminiModelTrait(agent.GeminiModel)}");
            if (isGeminiUnavailable) warningLines.Add("⚠️ Gemini CLIが見つかりません。Settings画面でパスを確認してください。");
        }
        if (_duplicateFilePaths.Contains(agent.FilePath))
        {
            warningLines.Add("⚠️ 他のメンバーとIdまたは呼び名が重複しています。編集から名前を変更してください。");
        }
        // マウスオーバーで役割名＋説明文の全文を折り返し表示する(カード全体に1つだけ付ける。
        // 子要素(未接続の警告・モデル行)の個別ToolTipはそれぞれの上にマウスがあるときだけ優先される)
        var memberToolTip = CreateMemberCardToolTip(agent, roleText.Text, modelLines, warningLines);
        memberToolTip.Style = (Style)FindResource("RoundedToolTipStyle");
        AttachFollowingToolTip(card, memberToolTip);

        rootGrid.Children.Add(textArea);

        // 6. 完了後の光る枠(普段は非表示)。枠の太さでカードの中身がずれないよう、カードの枠線ではなく
        //    中身の上に重ねる。クリックやドラッグの邪魔をしないよう当たり判定は無し。
        rootGrid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(CardCornerRadius - CardBorderThickness),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Tag = GlowFrameTag,
        });

        card.Child = rootGrid;

        // ホバー演出: 装飾面のみゆるさを出す設計のため、枠線のアクセント色変化に留める。
        // 完了後の光が出ている間は、光の色を上書きしない。
        card.MouseEnter += (_, _) =>
        {
            if (!CardGlowEffect.IsShown(card))
            {
                card.BorderBrush = CardBorderHoverAccent;
            }
        };
        card.MouseLeave += (_, _) =>
        {
            if (!CardGlowEffect.IsShown(card))
            {
                card.BorderBrush = CardBorderDefault;
            }
        };

        // カードが画面に載ったら(新規作成・再生成・チーム切り替え)、作業中の揺れ・完了後の光・バッジを今の状態で復元する。
        // 画面から外れたらアニメーションを止める(並び替えで一時的に外れても、載り直したときに復元される)。
        card.Loaded += (_, _) => ApplyCardPresentation(card, agent, _activityMonitor.GetState(agent.Name));
        card.Unloaded += (_, _) => SuspendCardEffects(card);

        // カードの右クリックでも「⋮」と同じメニューを開く(呼びかけ文コピー等)
        card.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCardContextMenu(agent, card, ownerLabel);
        };

        card.MouseLeftButtonUp += (_, e) =>
        {
            // F-3: ドラッグ操作の末尾で発火するクリックは詳細ポップアップを開かない
            if (_isCardDragging) return;

            // 完了後に光っているカードのクリックは「確認した」扱いにして光を消すだけにし、詳細は開かない
            // (クリックを消費する)。「⋮」ボタンのクリックはそちらで処理済み(Handled)のためここには来ない。
            if (_cardAttention.Acknowledge(agent.Name))
            {
                e.Handled = true;
                RefreshCardPresentationsFor(agent.Name);

                // Dashboard上部の完了カードは、確認したら消える
                RefreshDashboardActivity(reloadEntries: false);
                return;
            }

            OpenDetailPopup(agent, ownerLabel, sourceClaudeDir);
        };

        // F-3: カードをドラッグしてサイドバーのチーム/グループにドロップするとメンバー異動になる
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _cardDragStartPoint = e.GetPosition(null);
            _isCardDragging = false;
        };
        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isCardDragging)
            {
                return;
            }

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _cardDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _cardDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (card.Tag is not AgentDefinition dragAgent)
            {
                return;
            }

            _isCardDragging = true;
            try
            {
                DragDrop.DoDragDrop(card, new DataObject(typeof(AgentDefinition), dragAgent), DragDropEffects.Move);
            }
            finally
            {
                _isCardDragging = false;
            }
        };

        return card;
    }



    private void ShowCardContextMenu(AgentDefinition agent, FrameworkElement target, string ownerLabel)
    {
        var menu = new ContextMenu();

        // 👁️ 詳細
        var viewItem = new MenuItem { Header = "👁️ 詳細を見る" };
        viewItem.Click += (_, _) => OpenDetailPopup(agent, ownerLabel);
        menu.Items.Add(viewItem);

        // ✏️ 編集
        var editItem = new MenuItem { Header = "✏️ エージェントを編集" };
        editItem.Click += (_, _) =>
        {
            var targetDir = Path.GetDirectoryName(agent.FilePath)!;
            var editWin = new AgentEditWindow(agent, targetDir, string.Empty, GetScopeAgentsProvider(targetDir)) { Owner = this };
            editWin.ShowDialog();
            if (editWin.Saved)
            {
                SelectScope(_currentScope);
            }
        };
        menu.Items.Add(editItem);

        // 📋 複製 (クローン)
        var cloneItem = new MenuItem { Header = "📋 複製 (クローン)" };
        cloneItem.Click += (_, _) => CloneAgent(agent);
        menu.Items.Add(cloneItem);

        // 📣 呼びかけ文をコピー(クリップボードへのコピーのみ。AIやコマンドは起動しない)
        var copyCallPhraseItem = new MenuItem { Header = "📣 呼びかけ文をコピー" };
        copyCallPhraseItem.Click += (_, _) => CopyCallPhrase(agent);
        menu.Items.Add(copyCallPhraseItem);


        menu.Items.Add(new Separator());

        // 🗑️ 削除
        var deleteItem = new MenuItem { Header = "🗑️ 削除", Foreground = Brushes.Crimson };
        deleteItem.Click += (_, _) => DeleteAgentWithConfirm(agent);
        menu.Items.Add(deleteItem);

        ApplyRoundedMenuStyle(menu);
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    /// <summary>
    /// エージェントへの呼びかけ文(例:「software-engineer エージェント（ツクル）に、次の作業を頼んでください：」)を
    /// クリップボードへコピーし、画面に「コピーしました」を短く表示する。
    /// クリップボードが他アプリに使用中などで失敗しても例外で落とさず「コピーできませんでした」と表示する。
    /// </summary>
    /// <param name="agent">呼びかけ対象のエージェント定義。</param>
    private void CopyCallPhrase(AgentDefinition agent)
    {
        var settings = AppSettingsLoader.Load();
        var phrase = CallPhraseBuilder.Build(agent, settings);

        try
        {
            // SetDataObject(copy: true)はアプリ終了後もクリップボードに内容を残す
            Clipboard.SetDataObject(phrase, true);
            ShowTransientMessage("📋 コピーしました", settings.EffectiveCopyFeedbackDurationMilliseconds);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            // COMException(CLIPBRD_E_CANT_OPEN 等)は ExternalException の派生
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] クリップボードへのコピーに失敗: {ex.Message}");
            ShowTransientMessage("⚠️ コピーできませんでした（他のアプリがクリップボードを使用中です）", settings.EffectiveCopyFeedbackDurationMilliseconds);
        }
    }

    /// <summary>
    /// 画面下部に短いメッセージを一時表示し、指定時間後に自動で消す。
    /// 表示中に再度呼ばれた場合は文言を差し替え、表示時間を延長する。
    /// </summary>
    /// <param name="message">表示する文言。</param>
    /// <param name="durationMilliseconds">表示しておく時間(ミリ秒)。</param>
    private void ShowTransientMessage(string message, int durationMilliseconds)
    {
        TransientMessageText.Text = message;
        TransientMessageBorder.Visibility = Visibility.Visible;

        _transientMessageTimer ??= CreateTransientMessageTimer();
        _transientMessageTimer.Stop();
        _transientMessageTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
        _transientMessageTimer.Start();
    }

    /// <summary>
    /// 一時表示を消すためのタイマーを生成する(1回発火したら止まる)。
    /// </summary>
    /// <returns>生成したタイマー。</returns>
    private DispatcherTimer CreateTransientMessageTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TransientMessageBorder.Visibility = Visibility.Collapsed;
        };
        return timer;
    }

    private void CloneAgent(AgentDefinition agent)
    {
        var targetDir = Path.GetDirectoryName(agent.FilePath)!;
        var studioAgents = AgentNameUniqueness.InScope(GetAllStudioAgents(), targetDir).ToList();

        // BUG-18: 複製後もスタジオ全体でIdと呼び名が重複しないよう、空いている名前を探す
        var newName = $"{agent.Name}-copy";
        var count = 2;
        while (File.Exists(Path.Combine(targetDir, $"{newName}.md")) ||
               Directory.Exists(Path.Combine(targetDir, newName)) ||
               AgentNameUniqueness.FindConflict(newName, null, studioAgents) is not null)
        {
            newName = $"{agent.Name}-copy{count}";
            count++;
        }

        var newDisplayName = AgentNameUniqueness.SuggestCopyDisplayName(agent.EffectiveDisplayName, studioAgents.ToList());

        if (agent.Engine == AgentEngineKind.Gemini && agent.FilePath.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            // Geminiスキルのフォルダ複製
            var parentDir = Path.GetDirectoryName(targetDir)!;
            var newSkillDir = Path.Combine(parentDir, newName);
            Directory.CreateDirectory(newSkillDir);
            var newSkillFile = Path.Combine(newSkillDir, "SKILL.md");
            var content = File.ReadAllText(agent.FilePath);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"^name:\s*[^\r\n]+", $"name: {newName}", System.Text.RegularExpressions.RegexOptions.Multiline);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"^displayName:\s*[^\r\n]+", $"displayName: {newDisplayName}", System.Text.RegularExpressions.RegexOptions.Multiline);
            File.WriteAllText(newSkillFile, content);

            // 元の画像(ユーザー設定または同梱画像)を複製先のId名でコピーして、画像を引き継ぐ
            AgentCloneAvatarCopier.CopyForClone(
                agent,
                new AgentDeployPlan(AgentEngineKind.Gemini, null, newSkillDir, newSkillFile, []),
                targetDir,
                newName);
        }
        else
        {
            // Claudeエージェントの複製
            var newFilePath = Path.Combine(targetDir, $"{newName}.md");
            AgentDefinitionLoader.Save(
                newFilePath,
                newName,
                agent.Description,
                agent.Tools,
                agent.Model,
                agent.Color,
                agent.Body,
                newDisplayName,
                auxGeminiModel: agent.GeminiModel);

            // 元の画像(ユーザー設定または同梱画像)を複製先のId名でコピーして、画像を引き継ぐ
            AgentCloneAvatarCopier.CopyForClone(
                agent,
                new AgentDeployPlan(AgentEngineKind.Claude, newFilePath, null, null, []),
                targetDir,
                newName);
        }

        RefreshMemberCards();
    }

    private void DeleteAgentWithConfirm(AgentDefinition agent)
    {
        var result = MessageBox.Show(
            this,
            $"エージェント「{agent.EffectiveDisplayName}」({agent.Name}) を削除しますか？\nこの操作は元に戻せません。",
            "エージェントの削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var (claudeDir, geminiDir, scopeType) = GetCurrentScopeAgentDirs();
            var scopeAgents = AgentDefinitionLoader.LoadScopeAgents(claudeDir, geminiDir, scopeType);
            AgentDefinitionLoader.DeleteAgentAndOrphanedAvatar(agent, scopeAgents);

            RefreshMemberCards();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// A案モックアップ完全再現の「＋ Add New Member」縦長カード。
    /// </summary>
    private FrameworkElement BuildAddMemberCard(ScopeContext scope, string targetAgentsDir)
    {
        var dashedBorder = new Rectangle
        {
            Stroke = (SolidColorBrush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeThickness = 1.5,
            RadiusX = CardCornerRadius,
            RadiusY = CardCornerRadius,
            Fill = (SolidColorBrush)new BrushConverter().ConvertFromString("#F8FAFC")!,
        };

        var plusIcon = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            Margin = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "＋",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#475569")!,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var labelText = new TextBlock
        {
            Text = "+ Add New Member",
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#1E293B")!,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var subText = new TextBlock
        {
            Text = "Add Agent",
            FontSize = 12,
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var contentStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        contentStack.Children.Add(plusIcon);
        contentStack.Children.Add(labelText);
        contentStack.Children.Add(subText);

        var grid = new Grid
        {
            Width = CardWidth,
            Height = CardHeight,
            Margin = new Thickness(0, 0, CardGapHorizontal, CardGapVertical),
            Cursor = Cursors.Hand,
        };
        grid.Children.Add(dashedBorder);
        grid.Children.Add(contentStack);

        grid.MouseEnter += (_, _) =>
        {
            dashedBorder.Stroke = (SolidColorBrush)new BrushConverter().ConvertFromString("#6366F1")!;
            dashedBorder.Fill = (SolidColorBrush)new BrushConverter().ConvertFromString("#F5F3FF")!;
            labelText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#4F46E5")!;
            plusIcon.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#EEF2FF")!;
        };
        grid.MouseLeave += (_, _) =>
        {
            dashedBorder.Stroke = (SolidColorBrush)new BrushConverter().ConvertFromString("#CBD5E1")!;
            dashedBorder.Fill = (SolidColorBrush)new BrushConverter().ConvertFromString("#F8FAFC")!;
            labelText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#1E293B")!;
            plusIcon.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#E2E8F0")!;
        };

        grid.MouseLeftButtonUp += (_, _) =>
        {
            var editWindow = new AgentEditWindow(null, targetAgentsDir, $"{scope.DisplayName}に追加", GetScopeAgentsProvider(targetAgentsDir)) { Owner = this };
            editWindow.ShowDialog();
            if (editWindow.Saved)
            {
                SelectScope(_currentScope);
            }
        };

        return grid;
    }

    // ---- イベントハンドラ・ダイアログ ----

    private void AgentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchFilter = AgentSearchBox.Text.Trim();
        AgentSearchPlaceholder.Visibility = string.IsNullOrEmpty(AgentSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshMemberCards();
    }

    private void AgentSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AgentSearchBox.Text = string.Empty;

            // G-7: フォーカスを完全に外す(Keyboard.ClearFocus)のではなく、
            // カード一覧の先頭カードへ明示的に移すことでキーボード操作をスムーズに続けられるようにする。
            var firstCard = FindMemberCards(this).FirstOrDefault();
            if (firstCard is not null)
            {
                firstCard.Focusable = true;
                firstCard.Focus();
            }
            else
            {
                Keyboard.ClearFocus();
            }

            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+F または Ctrl+K で検索バーにフォーカス
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.F || e.Key == Key.K)
            {
                AgentSearchBox.Focus();
                AgentSearchBox.SelectAll();
                e.Handled = true;
            }
        }

        // F5 で外部変更を即時反映 (F-1)
        if (e.Key == Key.F5)
        {
            RefreshCurrentScopeManually();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 外部（アプリ外）でのファイル追加・編集・削除を今すぐ画面に反映する (F-1)。
    /// ポーリングタイマーの周期を待たずに、リーダーバナー・リーダー枠・メンバーカードを再読み込みする。
    /// あわせてCLI導入状況(B-2)も再検出し、インストール後に未導入バナーが残り続けないようにする。
    /// </summary>
    private void ManualRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshCurrentScopeManually();
        _ = RefreshCliAvailabilityAsync();
    }

    private void RefreshCurrentScopeManually()
    {
        LoadRegisteredGroups();
        ReloadSidebar();
        RefreshLeaderBanner();
        RefreshActiveLeaders();
        RefreshMemberCards();
    }

    private void DepartmentNavButton_Click(object sender, RoutedEventArgs e)
    {
        SelectScope(CreateDepartmentScope());
    }

    private void ScreenCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"スクリーンショットツールの起動に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ScopeRuleButton_Click(object sender, RoutedEventArgs e)
    {
        OpenGlobalRuleDialog();
    }



    private void ImportTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var (claudeDir, geminiSkillsDir, _) = GetCurrentScopeAgentDirs();
        var scopeName = _currentScope.DisplayName;

        var window = new AgentTemplateImportWindow(claudeDir, scopeName, GetScopeAgentsProvider(claudeDir), geminiSkillsDir, GetAllStudioAgents) { Owner = this };
        if (window.ShowDialog() == true && window.ImportedCount > 0)
        {
            SelectScope(_currentScope);
        }
    }

    private void EmptyAddMemberButton_Click(object sender, RoutedEventArgs e)
    {
        var (claudeDir, _, _) = GetCurrentScopeAgentDirs();
        var editWindow = new AgentEditWindow(null, claudeDir, $"{_currentScope.DisplayName}に追加", GetScopeAgentsProvider(claudeDir)) { Owner = this };
        editWindow.ShowDialog();
        if (editWindow.Saved)
        {
            SelectScope(_currentScope);
        }
    }

    private void ManageTeamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentScope.Kind == ScopeKind.Department) return;

        var title = ScopeRuleLabelFormatter.Format(
            _currentScope.Kind == ScopeKind.Team ? ScopeRuleKind.Team : ScopeRuleKind.Group,
            _currentScope.DisplayName);

        // 親スコープのルールを継承・確認できる導線 (U-4)。
        // Team選択時は親ワークスペースへ、Group選択時はグローバルルールへリンクする。
        string? parentLabel = null;
        Action? openParent = null;

        if (_currentScope.Kind == ScopeKind.Team)
        {
            var parentGroup = _groups.FirstOrDefault(g => g.RootPath == _currentScope.ParentGroupPath);
            if (parentGroup is not null)
            {
                parentLabel = $"⬆ 親ワークスペース「{parentGroup.DisplayName}」のルールを見る";
                openParent = () => OpenGroupRuleDialog(parentGroup);
            }
        }
        else if (_currentScope.Kind == ScopeKind.Group)
        {
            parentLabel = "🌐 グローバルルールを見る";
            openParent = OpenGlobalRuleDialog;
        }

        OpenRuleDialog(title, _currentScope.RulePath, parentLabel, openParent);
    }

    private void OpenGlobalRuleDialog()
    {
        var globalGeminiPath = ResolveGlobalGeminiMdPath();
        new RulePopupWindow("🌐 グローバルルール (全体共通)", GlobalClaudeMdPath, globalGeminiPath) { Owner = this }.ShowDialog();
    }

    private void OpenGroupRuleDialog(GroupItem group)
    {
        var claudeMdPath = Path.Combine(group.RootPath, "CLAUDE.md");
        var geminiMdPath = Path.Combine(group.RootPath, "GEMINI.md");
        new RulePopupWindow(
            ScopeRuleLabelFormatter.Format(ScopeRuleKind.Group, group.DisplayName),
            claudeMdPath,
            File.Exists(geminiMdPath) ? geminiMdPath : null,
            "🌐 グローバルルールを見る",
            OpenGlobalRuleDialog) { Owner = this }.ShowDialog();
    }

    private static string ResolveGlobalGeminiMdPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(userProfile, ".gemini", "GEMINI.md"),
            Path.Combine(userProfile, ".gemini", "config", "GEMINI.md"),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private void OpenRuleDialog(string title, string claudeMdPath, string? parentRuleLinkLabel = null, Action? openParentRule = null)
    {
        var geminiMdPath = ResolveGeminiMdPath(_currentScope, claudeMdPath);
        new RulePopupWindow(title, claudeMdPath, geminiMdPath, parentRuleLinkLabel, openParentRule) { Owner = this }.ShowDialog();
    }

    private string ResolveGeminiMdPath(ScopeContext scope, string claudeMdPath)
    {
        // 1. スコープフォルダが指定されている場合(グループ/チーム)
        if (!string.IsNullOrWhiteSpace(scope.FolderPath))
        {
            var path = Path.Combine(scope.FolderPath, "GEMINI.md");
            if (File.Exists(path))
            {
                return path;
            }
        }

        // 2. CLAUDE.mdと同階層
        if (!string.IsNullOrWhiteSpace(claudeMdPath))
        {
            var dir = Path.GetDirectoryName(claudeMdPath);
            if (dir != null)
            {
                var sameDirGemini = Path.Combine(dir, "GEMINI.md");
                if (File.Exists(sameDirGemini))
                {
                    return sameDirGemini;
                }
            }
        }

        // 3. 登録されているグループ(ワークスペース)直下のGEMINI.md
        foreach (var g in _groups)
        {
            var groupGemini = Path.Combine(g.RootPath, "GEMINI.md");
            if (File.Exists(groupGemini))
            {
                return groupGemini;
            }
        }

        // 4. グローバルGemini設定へのフォールバック
        return ResolveGlobalGeminiMdPath();
    }

    /// <summary>
    /// CLI未導入案内バナー(U-13)内の「公式インストール手順を開く」ボタン押下時の処理。
    /// 無条件で2タブを自動オープンするとユーザーの意図しないブラウザ操作になるため、
    /// 案内文にURLをテキストとして表示するに留め、ブラウザを自動で開くことはしない
    /// (シラベ指摘対応)。
    /// </summary>
    /// <param name="sender">イベント送信元のボタン。</param>
    /// <param name="e">ルーティングイベント引数。</param>
    private void OpenCliInstallDocsButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "以下の公式インストールページを、必要な方だけブラウザで開いてご確認ください。\n\n" +
            "・Claude Code: https://claude.com/product/claude-code\n" +
            "・Gemini (Antigravity) CLI「agy」: https://antigravity.google/docs/cli\n\n" +
            "いずれかをインストール後、アプリを再起動するか「🔄 更新」を押してください。",
            "公式インストール手順",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// サイドバー下部の「Support」ボタン。ヘルプ画面をメイン画面の中央に表示する。
    /// </summary>
    private void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        new SupportWindow(this).ShowDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = this };
        settingsWindow.ShowDialog();
        if (settingsWindow.Saved)
        {
            // CLIパスが変更された可能性があるため、利用可否を再検出する (U-24)
            _ = RefreshCliAvailabilityAsync();

            // 作業中の揺れのオン/オフ等が変わった可能性があるため、カードの演出を新しい設定で出し直す
            _cardEffectSettings = AppSettingsLoader.Load();
            ApplyRecentConversationSettings();
            ReapplyAllCardEffects();
        }
    }

    /// <summary>
    /// Claude/Gemini CLIが実際に利用可能かを検出し、メンバーカード等の「⚠️ 未接続」表示に反映する (U-24)。
    /// </summary>
    private async Task RefreshCliAvailabilityAsync()
    {
        var settings = AppSettingsLoader.Load();
        var claudeTask = CliAvailability.IsAvailableAsync(settings.EffectiveClaudeCliPath);
        var geminiTask = CliAvailability.IsAvailableAsync(settings.EffectiveGeminiCliPath);
        await Task.WhenAll(claudeTask, geminiTask);

        var changed = _isClaudeCliAvailable != claudeTask.Result || _isGeminiCliAvailable != geminiTask.Result;
        _isClaudeCliAvailable = claudeTask.Result;
        _isGeminiCliAvailable = geminiTask.Result;

        // Claude/Geminiのいずれも未検出の場合のみ、CLI未導入案内バナーを表示する (U-13)
        NoCliDetectedWarningBorder.Visibility = (!_isClaudeCliAvailable && !_isGeminiCliAvailable)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (changed)
        {
            // 可用性が変わった場合、既存カードは差分更新(U-22)で使い回されて中身が古いままになるため、
            // このタイミングだけキャッシュを破棄して全カードを作り直す (U-24)。
            MembersGridPanel.Children.Clear();
            _memberSections.Clear();
            _memberCardsBySection.Clear();
        }

        RefreshMemberCards();
        RefreshActiveLeaders();
    }

    private void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "ワークスペースとして登録する作業ルートフォルダを選択してください" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_groups.Any(g => string.Equals(g.RootPath, dialog.FolderName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "そのフォルダは既にワークスペースとして登録されています。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newGroup = new GroupItem
        {
            RootPath = dialog.FolderName,
            DisplayName = ExtractName(Path.Combine(dialog.FolderName, "CLAUDE.md"), dialog.FolderName),
        };
        _groups.Add(newGroup);
        GroupRegistry.SaveWorkspaces(_groups.Select(g => g.RootPath).ToList(), GlobalClaudeMdPath);

        ReloadSidebar();
        SelectScope(new ScopeContext(
            ScopeKind.Group,
            newGroup.DisplayName,
            newGroup.RootPath,
            null,
            Path.Combine(newGroup.RootPath, "CLAUDE.md")));
    }

    private void UnregisterWorkspace(GroupItem group)
    {
        var result = MessageBox.Show(
            this,
            $"ワークスペース「{group.DisplayName}」の登録をStudioから解除しますか？\n\n※フォルダ内のファイルは削除されず、Studioの管理一覧からのみ除外されます。\nなお、グローバルルール(~/.claude/CLAUDE.md、~/.gemini/GEMINI.md)の「## グループ」節からも外れます。",
            "ワークスペースの登録解除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _groups.Remove(group);
        GroupRegistry.RemoveWorkspace(group.RootPath, GlobalClaudeMdPath);

        // もし現在選択中のスコープがこのグループまたはその配下のチームだった場合は全社へ戻す
        if ((_currentScope.Kind == ScopeKind.Group && _currentScope.FolderPath == group.RootPath) ||
            (_currentScope.Kind == ScopeKind.Team && _currentScope.ParentGroupPath == group.RootPath))
        {
            SelectScope(CreateDepartmentScope());
        }

        ReloadSidebar();
    }

    private void AddTeamToGroup(GroupItem group)
    {
        var window = new CreateTeamWindow(group.RootPath, group.DisplayName) { Owner = this };
        if (window.ShowDialog() != true || string.IsNullOrEmpty(window.CreatedTeamPath))
        {
            return;
        }

        var teamPath = window.CreatedTeamPath;
        var folderName = Path.GetFileName(teamPath);

        LoadRegisteredGroups();
        ReloadSidebar();

        var teamItem = _groups.FirstOrDefault(g => g.RootPath == group.RootPath)?.Teams.FirstOrDefault(t => t.RootPath == teamPath);
        if (teamItem is not null)
        {
            SelectScope(new ScopeContext(
                ScopeKind.Team,
                teamItem.DisplayName,
                teamItem.RootPath,
                teamItem.ParentGroupPath,
                Path.Combine(teamItem.RootPath, "CLAUDE.md")));
        }

        // 続けて最初のメンバーを追加するか尋ねる
        var askMember = MessageBox.Show(
            this,
            $"チーム「{folderName}」を作成しました！\nこのチームに最初のメンバー（エージェント）を追加しますか？",
            "チーム作成完了",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (askMember == MessageBoxResult.Yes)
        {
            var (claudeDir, _, _) = GetCurrentScopeAgentDirs();
            var editWindow = new AgentEditWindow(null, claudeDir, $"チーム「{folderName}」に追加", GetScopeAgentsProvider(claudeDir)) { Owner = this };
            editWindow.ShowDialog();
            if (editWindow.Saved)
            {
                SelectScope(_currentScope);
            }
        }
    }

    /// <summary>
    /// メンバーの詳細画面を開く。
    /// </summary>
    /// <param name="agent">表示するメンバー。</param>
    /// <param name="ownerLabel">所属の表示名。</param>
    /// <param name="sourceClaudeDir">メンバーの所属先の.claude/agentsフォルダ。nullなら表示中の画面の範囲を使う。</param>
    private void OpenDetailPopup(AgentDefinition agent, string ownerLabel, string? sourceClaudeDir = null)
    {
        var (claudeDir, geminiDir, scopeType) = sourceClaudeDir is null
            ? GetCurrentScopeAgentDirs()
            : ResolveScopeDirs(sourceClaudeDir);
        var scopeAgents = AgentDefinitionLoader.LoadScopeAgents(claudeDir, geminiDir, scopeType);
        var detailWindow = new AgentDetailWindow(agent, ownerLabel, scopeAgents, GetScopeAgentsProvider(claudeDir)) { Owner = this };
        detailWindow.ShowDialog();
        if (detailWindow.Changed)
        {
            SelectScope(_currentScope);
        }
    }

    // ---- ユーティリティ ----

    private static string ExtractName(string claudeMdPath, string fallbackRootPath)
    {
        var fallback = Path.GetFileName(fallbackRootPath.TrimEnd(Path.DirectorySeparatorChar));
        return ExtractNameWithFallback(claudeMdPath, fallback);
    }

    /// <summary>
    /// CLAUDE.mdの第1見出しから表示名の動的取得を試み、取得できなければ<paramref name="fallback"/>をそのまま返す(D-3)。
    /// <see cref="ExtractName"/>はフォールバックをフォルダ名から導出するが、こちらは呼び出し側が
    /// 任意のフォールバック文字列(例:「全社」)を直接指定できる。
    /// </summary>
    private static string ExtractNameWithFallback(string claudeMdPath, string fallback)
    {
        if (!File.Exists(claudeMdPath))
        {
            return fallback;
        }

        foreach (var line in File.ReadLines(claudeMdPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                var name = trimmed[2..].Trim();
                return SanitizeScopeDisplayName(name, fallback);
            }
        }

        return fallback;
    }

    /// <summary>
    /// 「全社」スコープのScopeContextを生成する(D-3, R-2)。
    /// グローバル指示の見出し（"グローバル指示"等）に引っ張られず、所属ラベルと
    /// 統一された名称「Dashboard」を既定表示名とする。
    /// </summary>
    private static ScopeContext CreateDepartmentScope() => new(
        ScopeKind.Department,
        DashboardActivityCollector.DashboardName,
        null,
        null,
        GlobalClaudeMdPath);

    /// <summary>
    /// CLAUDE.md等の第1見出しから注釈括弧やサブタイトルを除去し、サイドバー表示用の簡潔な名称に整える。
    /// 例: "AgentDeskApp 開発チーム (CLAUDE.md / GEMINI.md 共通)" -> "AgentDeskApp 開発チーム"
    /// 例: "Sloth Studio ワークスペース方針 (CLAUDE.md / GEMINI.md 共通)" -> "Sloth Studio"
    /// </summary>
    private static string SanitizeScopeDisplayName(string rawName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return fallback;
        }

        var name = rawName.Trim();

        // (CLAUDE.md / GEMINI.md 共通) 等の注釈括弧を除去
        name = Regex.Replace(name, @"[\(（].*?(?:CLAUDE|GEMINI|共通|方針).*?[\)）]", "", RegexOptions.IgnoreCase).Trim();

        // 「―」「—」以降のサブタイトル・説明文を除去（例: "Java案件復帰学習 ― 作業スペース方針" -> "Java案件復帰学習"）
        var dashIndex = name.IndexOfAny(['―', '—']);
        if (dashIndex > 0)
        {
            name = name[..dashIndex].Trim();
        }

        // 末尾の「ワークスペース方針」「作業スペース方針」等の定型辞を除去
        name = Regex.Replace(name, @"(?:ワークスペース|作業スペース)?方針$", "").Trim();

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string GetInitial(string? displayName) => AvatarInitialStyle.GetInitial(displayName);

}
