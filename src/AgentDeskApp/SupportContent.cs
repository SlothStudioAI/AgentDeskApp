using System.Reflection;

namespace AgentDeskApp;

/// <summary>
/// ヘルプ(Support)画面の1セクション分(見出し・本文・任意のコピー用文字列)。
/// </summary>
/// <param name="Heading">セクションの見出し。</param>
/// <param name="Body">セクションの本文(改行で段落分け)。</param>
/// <param name="CopyableText">選択・コピーできるように別枠で表示する文字列(パス等)。無ければnull。</param>
/// <param name="Links">本文の下に並べる案内項目(使い方動画・ご連絡など)。無ければnull。</param>
public sealed record SupportSection(
    string Heading, string Body, string? CopyableText = null, IReadOnlyList<SupportLinkItem>? Links = null);

/// <summary>
/// ヘルプ画面の案内項目1つ分(ラベル・案内文・任意のリンク先)。
/// </summary>
/// <param name="Label">項目名(例: 使い方動画)。</param>
/// <param name="Text">案内文。</param>
/// <param name="Url">リンク先。リンクを出さない場合はnull(空文字・不正な値は<see cref="SupportContent.CreateLinkItem"/>でnullになる)。</param>
public sealed record SupportLinkItem(string Label, string Text, string? Url)
{
    /// <summary>クリックで開けるリンクとして表示するか(URLが有効な場合のみtrue)。</summary>
    public bool HasLink => Url is not null;
}

/// <summary>
/// ヘルプ(Support)画面の本文を集約するクラス。文言はすべてここの定数にまとめ、画面側(SupportWindow)には直書きしない。
/// </summary>
public static class SupportContent
{
    /// <summary>ウィンドウのタイトル。</summary>
    public const string WindowTitle = "Support — ヘルプ";

    /// <summary>画面上部の見出し。</summary>
    public const string HeaderText = "Sloth Studio ヘルプ";

    /// <summary>パスをコピーするボタンの文言。</summary>
    public const string CopyButtonText = "📋 コピー";

    /// <summary>コピー完了後に一時表示するボタンの文言。</summary>
    public const string CopiedButtonText = "✅ コピーしました";

    /// <summary>閉じるボタンの文言。</summary>
    public const string CloseButtonText = "閉じる";

    /// <summary>バージョンが取得できなかったときの表示。</summary>
    public const string UnknownVersionText = "不明";

    /// <summary>設定ファイルのパスが取得できなかったときの表示。</summary>
    public const string UnknownPathText = "(場所を取得できませんでした)";

    /// <summary>
    /// 「困ったときは」の案内文。不具合やご要望の連絡先の案内。文言を変えるときは、ここだけを差し替える。
    /// </summary>
    public const string SupportContactText = "不具合やご要望は、GitHub の Issues でお知らせください。";

    /// <summary>
    /// 使い方動画(YouTube)のURL。公開時にここへURLを入れる。空のままなら案内文のみを表示しリンクは出さない。
    /// </summary>
    public const string SupportYouTubeUrl = "";

    /// <summary>使い方動画の案内文。</summary>
    public const string SupportYouTubeText = "使い方は YouTube で公開予定です。";

    /// <summary>
    /// 連絡先(GitHub Issues)のURL。空にすると案内文のみを表示しリンクは出さない。
    /// </summary>
    public const string SupportIssuesUrl = "https://github.com/SlothStudioAI/AgentDeskApp/issues";

    private const string YouTubeLabel = "使い方動画";
    private const string ContactLabel = "ご連絡";

    private const string AboutHeading = "Sloth Studio について";
    private const string AboutBody =
        "Sloth Studio は、AIサブエージェント(Claude Code / Gemini)のメンバーをチーム単位で管理し、" +
        "誰が作業中か・誰が完了したかを見える化するデスクトップアプリです。";

    private const string UsageHeading = "基本の使い方";
    private const string UsageBody =
        "・ワークスペース／チーム／メンバー: ワークスペース(フォルダ)の中にチームがあり、チームにメンバー(エージェント)が所属します。サイドバーで選ぶと、その範囲のメンバーが表示されます。\n" +
        "・メンバーカード: 右クリックまたは右上の「⋮」から、詳細・編集・複製・呼びかけ文のコピー・削除ができます。\n" +
        "・リーダーカード: クローディ(Claude)とジェミリー(Gemini)が、各AIのリーダーとして表示されます。\n" +
        "・Dashboard: 稼働中のメンバーと完了したメンバーが並びます。完了カードはクリックして確認すると消えます。\n" +
        "・テンプレート追加: 職種別テンプレートからメンバーを選び、配置先(Claude 専用 / Gemini 専用 / 両方)を決めて追加できます。\n" +
        "・ルール: ワークスペース／チーム／グローバルの各ルール(CLAUDE.md / GEMINI.md)を画面から確認・編集できます。";

    private const string SettingsHeading = "設定ファイルの場所";
    private const string SettingsBody = "アプリの設定は次のファイル(settings.json)に保存されます。";

    private const string DiagnosticLogHeading = "診断ログの場所";
    private const string DiagnosticLogBody =
        "診断ログは既定ではオフです。不具合を調べるときだけ、設定ファイル(settings.json)に \"DiagnosticLogEnabled\": true を追加して有効にします。" +
        "有効にすると、次のファイルに記録されます(有効にしたときに作られます)。記録するのはメンバー名・状態・件数・短縮セッションIDのみで、会話内容は含みません。" +
        "無効に戻すときは false にするか、項目を削除します。";

    private const string CommandsHeading = "アプリが実行する外部コマンド";
    private const string CommandsBody =
        "【自動で実行するもの】\n" +
        "・claude agents --json: 作業中セッションの確認のため、定期的に実行します(既定は30秒ごと。5秒で応答がなければ打ち切ります)。\n" +
        "【起動時・設定画面・更新ボタンで実行するもの】\n" +
        "・claude --version と agy --version(Gemini / Antigravity): CLIがインストールされているかの確認です。\n" +
        "・.cmd形式のCLIを呼び出すときは、cmd.exe /c を経由して実行します。\n" +
        "【メニューやボタンを押したときだけ実行するもの】\n" +
        "・エクスプローラー(フォルダを開く)\n" +
        "・画面の切り取り(ms-screenclip: スクリーンショット添付)\n" +
        "・ヘルプ内のリンクをクリックしたときの、既定のブラウザでのURL表示\n" +
        "上記以外のコマンドは実行しません。\n" +
        "【通信について】\n" +
        "このアプリ自体は外部へ通信しません。ただし、実行する claude や agy は、各ツールの仕様で通信する場合があります。";

    private const string HelpHeading = "困ったときは";

    /// <summary>
    /// バージョンの表示文字列を組み立てる(例: "バージョン 0.9.0")。取得できなければ「不明」とする。
    /// </summary>
    /// <param name="version">アセンブリのバージョン。取得できなかった場合はnull。</param>
    /// <returns>表示用のバージョン文字列。</returns>
    public static string FormatVersion(Version? version)
    {
        if (version is null)
        {
            return $"バージョン {UnknownVersionText}";
        }

        // 末尾のリビジョン番号(通常0)は表示しない。3桁目が未設定(-1)の場合は2桁で表示する
        var text = version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
        return $"バージョン {text}";
    }

    /// <summary>
    /// 実行中アプリのアセンブリバージョンを取得する。
    /// </summary>
    /// <returns>バージョン。取得できなければnull。</returns>
    public static Version? GetCurrentVersion() =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version;

    /// <summary>
    /// 設定ファイルのパスの表示文字列を返す。空・null の場合は取得失敗の案内文に置き換える。
    /// </summary>
    /// <param name="path">settings.json のパス。</param>
    /// <returns>表示用の文字列。</returns>
    public static string FormatSettingsPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? UnknownPathText : path;

    /// <summary>
    /// 案内項目を作る。URLが空・空白、またはhttp/https以外の場合はリンクなし(案内文のみ)の項目にする。
    /// </summary>
    /// <param name="label">項目名。</param>
    /// <param name="text">案内文。</param>
    /// <param name="url">リンク先の候補。</param>
    /// <returns>案内項目。</returns>
    public static SupportLinkItem CreateLinkItem(string label, string text, string? url)
    {
        var trimmed = url?.Trim();
        var valid = Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        return new SupportLinkItem(label, text, valid ? trimmed : null);
    }

    /// <summary>
    /// ヘルプ画面に表示するセクション一覧を組み立てる(使い方動画・連絡先のURLを引数で指定する版)。
    /// </summary>
    /// <param name="version">アセンブリのバージョン。</param>
    /// <param name="settingsPath">settings.json のパス。</param>
    /// <param name="youTubeUrl">使い方動画のURL。</param>
    /// <param name="issuesUrl">連絡先のURL。</param>
    /// <param name="diagnosticLogPath">診断ログのパス。nullなら診断ログの項目を出さない。</param>
    /// <returns>表示順のセクション配列。</returns>
    public static IReadOnlyList<SupportSection> Build(
        Version? version, string? settingsPath, string? youTubeUrl, string? issuesUrl, string? diagnosticLogPath = null)
    {
        var sections = new List<SupportSection>
        {
            new(AboutHeading, AboutBody + "\n" + FormatVersion(version)),
            new(UsageHeading, UsageBody),
            new(SettingsHeading, SettingsBody, FormatSettingsPath(settingsPath)),
        };

        // 診断ログの場所は、指定されたときだけ出す
        if (!string.IsNullOrWhiteSpace(diagnosticLogPath))
        {
            sections.Add(new SupportSection(DiagnosticLogHeading, DiagnosticLogBody, diagnosticLogPath));
        }

        sections.Add(new SupportSection(CommandsHeading, CommandsBody));
        sections.Add(new SupportSection(HelpHeading, string.Empty, null, new[]
        {
            CreateLinkItem(YouTubeLabel, SupportYouTubeText, youTubeUrl),
            CreateLinkItem(ContactLabel, SupportContactText, issuesUrl),
        }));
        return sections;
    }

    /// <summary>
    /// 定数(<see cref="SupportYouTubeUrl"/>等)を使ってセクション一覧を組み立てる。
    /// </summary>
    /// <param name="version">アセンブリのバージョン。</param>
    /// <param name="settingsPath">settings.json のパス。</param>
    /// <param name="diagnosticLogPath">診断ログのパス。nullなら診断ログの項目を出さない。</param>
    /// <returns>表示順のセクション配列。</returns>
    public static IReadOnlyList<SupportSection> Build(Version? version, string? settingsPath, string? diagnosticLogPath = null) =>
        Build(version, settingsPath, SupportYouTubeUrl, SupportIssuesUrl, diagnosticLogPath);
}
