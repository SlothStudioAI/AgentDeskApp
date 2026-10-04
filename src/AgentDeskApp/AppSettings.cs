using System.Text.Json.Serialization;

namespace AgentDeskApp;

/// <summary>
/// AgentDeskAppの設定値。変更される可能性がある数値はコードに直書きせず、この設定ファイル
/// (%APPDATA%\AgentDeskApp\settings.json)から読み込む方針とする(2026-09-25、コーディングルールとして決定)。
/// 「Effective〜」で始まるプロパティは保存値から計算した結果(不正値を既定値に置き換えたもの等)なので、
/// [JsonIgnore]で設定ファイルへは書き出さない。古いファイルに残っていても読み込み時は無視される。
/// </summary>
/// <param name="SessionRefreshIntervalSeconds">
/// <c>claude agents --json</c>を実行してbusyなセッション一覧を更新する周期(秒)。design.md §6.1。
/// </param>
/// <param name="LogPollIntervalSeconds">
/// busyなセッションの会話ログを追跡する周期(秒)。design.md §6.1。
/// </param>
/// <param name="ClaudeCliPath">Claude CLIの実行コマンドまたはパス(省略時はPATH解決→標準インストール場所を自動探索)。</param>
/// <param name="GeminiCliPath">Gemini/Antigravity CLIの実行コマンドまたはパス(省略時はPATH解決→標準インストール場所を自動探索)。</param>
/// <param name="Workspaces">登録されたワークスペース(グループ)のパス一覧。nullの場合はCLAUDE.mdから自動移行。</param>
/// <param name="MaxTaskHistoryRecords">
/// <see cref="AgentTaskHistoryReader"/>が1エージェントあたりに保持・返却するタスク履歴件数の上限(C-4+)。
/// </param>
/// <param name="TaskHistoryCacheSeconds">
/// <see cref="AgentTaskHistoryReader"/>のログ走査結果をメモリキャッシュしておく秒数(C-4+)。
/// 短時間に同じエージェントの履歴画面が繰り返し開かれても、この秒数内はディスク走査を再実行しない。
/// </param>
/// <param name="TaskHistoryMaxScanFiles">
/// <see cref="AgentTaskHistoryReader.ScanClaudeLogs"/>がプロジェクトディレクトリごとに走査対象とする
/// (更新日時降順の)jsonlファイル数の上限(シラベ指摘・マジックナンバー設定化)。
/// </param>
/// <param name="CompletionSoundEnabled">完了通知(Running→Done)時に通知音を鳴らすか。既定はオフ(うるさく感じる人もいるため、使いたい人が設定画面でオンにする)。</param>
/// <param name="CompletionSoundPath">
/// 完了時に鳴らす音ファイル(WAV)のパス。未指定・存在しない・読めない場合はWindows標準の通知音を使う。
/// </param>
/// <param name="AvatarBounceEnabled">完了時にカードのアバターを「ぷるん」と伸び縮みさせるか。既定はオン。</param>
/// <param name="CompletionBounceAmplitude">
/// 完了時の「ぷるん」の伸び縮みの強さ(倍率の増分。0.22なら最大22%横に広がる)。
/// 旧キー AvatarBounceAmplitude(既定0.12)は弱いという感想を受けて廃止し、保存済みの古い値に引きずられないよう新しいキーにした。
/// </param>
/// <param name="CompletionBounceCount">完了時の「ぷるん」を繰り返す回数。</param>
/// <param name="CompletionBounceDurationMilliseconds">完了時の「ぷるん」1回分の長さ(ミリ秒)。</param>
/// <param name="RunningSwayEnabled">作業中(Running)のメンバーのアバターをゆらゆら揺らし続けるか。既定はオン。</param>
/// <param name="RunningSwayAngleDegrees">作業中の揺れで左右に傾く最大角度(度)。下端中央を軸に傾く。</param>
/// <param name="RunningSwayPeriodMilliseconds">作業中の揺れの1往復(右→左→元)にかける時間(ミリ秒)。</param>
/// <param name="RunningSwayBreathAmplitude">作業中の揺れに重ねる縦方向の伸び縮みの強さ(倍率の増分)。</param>
/// <param name="CompletionGlowColor">完了後の光(カード枠と外側のぼんやりした光)の色。#RRGGBB または #AARRGGBB。</param>
/// <param name="CompletionGlowThickness">完了後の光の枠の太さ(ピクセル)。</param>
/// <param name="CompletionGlowBlurRadius">完了後のカードの外側に出すぼんやりした光の広がり(ぼかし半径)。</param>
/// <param name="CompletionGlowPulseMilliseconds">完了後の光をゆっくり明滅させる周期(ミリ秒、明→暗→明)。0なら明滅しない。</param>
/// <param name="TaskbarFlashEnabled">完了時、アプリが前面にない場合にタスクバーのアイコンを点滅させるか。既定はオン。</param>
/// <param name="CallPhraseTemplate">
/// 「呼びかけ文をコピー」でコピーする文のひな形(表示名がある場合)。{name}と{displayName}を置換する。
/// </param>
/// <param name="CallPhraseTemplateWithoutDisplayName">
/// 「呼びかけ文をコピー」でコピーする文のひな形(表示名が無い場合)。{name}を置換する。
/// </param>
/// <param name="CopyFeedbackDurationMilliseconds">「コピーしました」等の一時表示を出しておく時間(ミリ秒)。</param>
/// <param name="ClaudeLeaderCharacterName">TEAM欄のリーダーカードに大きく出す、Claudeリーダーのキャラ名。空欄なら既定(クローディ)。</param>
/// <param name="GeminiLeaderCharacterName">TEAM欄のリーダーカードに大きく出す、Geminiリーダーのキャラ名。空欄なら既定(ジェミリー)。</param>
/// <param name="CardBottomOverlayColor">
/// メンバー・リーダーカード下部に敷く、文字を読みやすくするための下地の色(#RRGGBB)。下ほど濃く、上に向かって透明になる。
/// 既定は白(2026-10-03、暗いグラデーションをやめて明るい下地に濃いグレーの文字を載せる方式に変更)。
/// </param>
/// <param name="CardBottomOverlayOpacity">カード下部の下地のいちばん下(最も濃いところ)の不透明度(0〜1)。0なら下地なし。</param>
/// <param name="CardTextColor">カード下部の文字(エージェント名・説明・モデル行・作業中など)の色。明るい下地の上で読める濃いグレー。</param>
/// <param name="ClaudeRecentConversationMinutes">
/// Claudeリーダーカードの「会話N件」に、待機中でも数える「直近の会話」の範囲(分)。会話ログ(jsonl)がこの分数以内に更新されていれば数える。
/// settings.jsonのみで変更する(スタジオ環境設定画面には出さない)。0以下なら既定値を使う。
/// </param>
/// <param name="GeminiRecentConversationMinutes">リーダーカードの「会話N件」にGeminiの会話を数える「直近」の範囲(分)。会話ログの更新がこの分数以内なら数える(既定5分、Claudeと同じ)。settings.jsonのみ。0以下なら既定値を使う。旧キーGeminiRecentConversationSecondsは無視される。</param>
/// <param name="AsyncSubagentMaxTrackMinutes">非同期(バックグラウンド)サブエージェントの完了通知(task-notification)を待ち続ける最大時間(分)。超えたら作業中を打ち切る安全弁。settings.jsonのみ。0以下なら既定値(30分)。</param>
/// <param name="DiagnosticLogEnabled">診断ログ(%APPDATA%\AgentDeskApp\logs\agentdesk-diagnostic.log)をファイルへ出すか。既定はオフ(キーが無ければ書かない)。不具合調査のときだけ settings.json で true にして有効化する。settings.jsonのみ。個人情報(プロンプト本文・パス全文・会話内容)は出さない。</param>
/// <param name="DiagnosticLogMaxBytes">診断ログ1ファイルのサイズ上限(バイト)。超えると「.1」へ退避(最大2ファイル)。settings.jsonのみ。0以下なら既定値(1MB)。</param>
/// <param name="MinRunningDisplaySeconds">作業中(Running)になったメンバーを、完了通知後も最低この秒数は作業中表示に保つ時間。短い作業でもカードの揺れが見えるようにする。settings.jsonのみ。負の値なら既定値(3秒)、0で無効。</param>
/// <param name="CardNameTextColor">カード下部の名前(呼び名・キャラ名)の色。他の文字より濃くして目立たせる。</param>
public sealed record AppSettings(
    int SessionRefreshIntervalSeconds = AppSettings.DefaultSessionRefreshIntervalSeconds,
    int LogPollIntervalSeconds = AppSettings.DefaultLogPollIntervalSeconds,
    string? ClaudeCliPath = null,
    string? GeminiCliPath = null,
    IReadOnlyList<string>? Workspaces = null,
    int MaxTaskHistoryRecords = AppSettings.DefaultMaxTaskHistoryRecords,
    int TaskHistoryCacheSeconds = AppSettings.DefaultTaskHistoryCacheSeconds,
    int TaskHistoryMaxScanFiles = AppSettings.DefaultTaskHistoryMaxScanFiles,
    bool CompletionSoundEnabled = false,
    string? CompletionSoundPath = null,
    bool AvatarBounceEnabled = true,
    double CompletionBounceAmplitude = AppSettings.DefaultCompletionBounceAmplitude,
    int CompletionBounceCount = AppSettings.DefaultCompletionBounceCount,
    int CompletionBounceDurationMilliseconds = AppSettings.DefaultCompletionBounceDurationMilliseconds,
    bool RunningSwayEnabled = true,
    double RunningSwayAngleDegrees = AppSettings.DefaultRunningSwayAngleDegrees,
    int RunningSwayPeriodMilliseconds = AppSettings.DefaultRunningSwayPeriodMilliseconds,
    double RunningSwayBreathAmplitude = AppSettings.DefaultRunningSwayBreathAmplitude,
    string? CompletionGlowColor = AppSettings.DefaultCompletionGlowColor,
    double CompletionGlowThickness = AppSettings.DefaultCompletionGlowThickness,
    double CompletionGlowBlurRadius = AppSettings.DefaultCompletionGlowBlurRadius,
    int CompletionGlowPulseMilliseconds = AppSettings.DefaultCompletionGlowPulseMilliseconds,
    bool TaskbarFlashEnabled = true,
    string? CallPhraseTemplate = AppSettings.DefaultCallPhraseTemplate,
    string? CallPhraseTemplateWithoutDisplayName = AppSettings.DefaultCallPhraseTemplateWithoutDisplayName,
    int CopyFeedbackDurationMilliseconds = AppSettings.DefaultCopyFeedbackDurationMilliseconds,
    string? ClaudeLeaderCharacterName = AppSettings.DefaultClaudeLeaderCharacterName,
    string? GeminiLeaderCharacterName = AppSettings.DefaultGeminiLeaderCharacterName,
    string? CardBottomOverlayColor = AppSettings.DefaultCardBottomOverlayColor,
    double CardBottomOverlayOpacity = AppSettings.DefaultCardBottomOverlayOpacity,
    string? CardTextColor = AppSettings.DefaultCardTextColor,
    string? CardNameTextColor = AppSettings.DefaultCardNameTextColor,
    int ClaudeRecentConversationMinutes = AppSettings.DefaultClaudeRecentConversationMinutes,
    int GeminiRecentConversationMinutes = AppSettings.DefaultGeminiRecentConversationMinutes,
    int AsyncSubagentMaxTrackMinutes = AppSettings.DefaultAsyncSubagentMaxTrackMinutes,
    bool DiagnosticLogEnabled = AppSettings.DefaultDiagnosticLogEnabled,
    long DiagnosticLogMaxBytes = AppSettings.DefaultDiagnosticLogMaxBytes,
    int MinRunningDisplaySeconds = AppSettings.DefaultMinRunningDisplaySeconds)
{
    /// <summary>診断ログの既定(オフ)。不具合調査のときだけ settings.json で true にして有効化する。</summary>
    public const bool DefaultDiagnosticLogEnabled = false;
    public const long DefaultDiagnosticLogMaxBytes = 1048576;
    public const int DefaultMinRunningDisplaySeconds = 3;
    public const int DefaultAsyncSubagentMaxTrackMinutes = 30;
    public const int DefaultClaudeRecentConversationMinutes = 5;
    public const int DefaultGeminiRecentConversationMinutes = 5;

    public const int DefaultSessionRefreshIntervalSeconds = 30;
    public const int DefaultLogPollIntervalSeconds = 5;
    public const int DefaultMaxTaskHistoryRecords = 50;
    public const int DefaultTaskHistoryCacheSeconds = 15;
    public const int DefaultTaskHistoryMaxScanFiles = 10;
    public const double DefaultCompletionBounceAmplitude = 0.22;
    public const int DefaultCompletionBounceCount = 5;
    public const int DefaultCompletionBounceDurationMilliseconds = 700;
    public const double DefaultRunningSwayAngleDegrees = 1.5;
    public const int DefaultRunningSwayPeriodMilliseconds = 2800;
    public const double DefaultRunningSwayBreathAmplitude = 0.045;
    public const string DefaultCompletionGlowColor = "#22C55E";
    public const double DefaultCompletionGlowThickness = 3;
    public const double DefaultCompletionGlowBlurRadius = 22;
    public const int DefaultCompletionGlowPulseMilliseconds = 1800;
    public const string DefaultCallPhraseTemplate = "{name} エージェント（{displayName}）に、次の作業を頼んでください：";
    public const string DefaultCallPhraseTemplateWithoutDisplayName = "{name} エージェントに、次の作業を頼んでください：";
    public const int DefaultCopyFeedbackDurationMilliseconds = 2000;
    public const string DefaultClaudeLeaderCharacterName = "クローディ";
    public const string DefaultGeminiLeaderCharacterName = "ジェミリー";
    public const string DefaultCardBottomOverlayColor = "#FFFFFF";
    public const double DefaultCardBottomOverlayOpacity = 0.9;
    public const string DefaultCardTextColor = "#374151";
    public const string DefaultCardNameTextColor = "#111827";

    /// <summary>有効なカード下部の下地の色。#RRGGBB / #AARRGGBB の形式でなければ既定(白)を使う。</summary>
    [JsonIgnore]
    public string EffectiveCardBottomOverlayColor => IsHexColor(CardBottomOverlayColor)
        ? CardBottomOverlayColor!.Trim()
        : DefaultCardBottomOverlayColor;

    /// <summary>有効なカード下部の下地の不透明度。0〜1の範囲外なら既定値を使う(0なら下地なし)。</summary>
    [JsonIgnore]
    public double EffectiveCardBottomOverlayOpacity => CardBottomOverlayOpacity is >= 0 and <= 1
        ? CardBottomOverlayOpacity
        : DefaultCardBottomOverlayOpacity;

    /// <summary>有効なカード下部の文字色。#RRGGBB / #AARRGGBB の形式でなければ既定(濃いグレー)を使う。</summary>
    [JsonIgnore]
    public string EffectiveCardTextColor => IsHexColor(CardTextColor)
        ? CardTextColor!.Trim()
        : DefaultCardTextColor;

    /// <summary>有効なカード下部の名前の文字色。#RRGGBB / #AARRGGBB の形式でなければ既定(さらに濃いグレー)を使う。</summary>
    [JsonIgnore]
    public string EffectiveCardNameTextColor => IsHexColor(CardNameTextColor)
        ? CardNameTextColor!.Trim()
        : DefaultCardNameTextColor;

    /// <summary>有効なClaudeリーダーのキャラ名。空欄や null の場合は既定(クローディ)を使う。前後の空白は除く。</summary>
    [JsonIgnore]
    public string EffectiveClaudeLeaderCharacterName => string.IsNullOrWhiteSpace(ClaudeLeaderCharacterName)
        ? DefaultClaudeLeaderCharacterName
        : ClaudeLeaderCharacterName.Trim();

    /// <summary>有効なGeminiリーダーのキャラ名。空欄や null の場合は既定(ジェミリー)を使う。前後の空白は除く。</summary>
    [JsonIgnore]
    public string EffectiveGeminiLeaderCharacterName => string.IsNullOrWhiteSpace(GeminiLeaderCharacterName)
        ? DefaultGeminiLeaderCharacterName
        : GeminiLeaderCharacterName.Trim();

    /// <summary>有効な呼びかけ文ひな形(表示名あり)。空欄や null の場合は既定のひな形を使う。</summary>
    [JsonIgnore]
    public string EffectiveCallPhraseTemplate => string.IsNullOrWhiteSpace(CallPhraseTemplate)
        ? DefaultCallPhraseTemplate
        : CallPhraseTemplate;

    /// <summary>有効な呼びかけ文ひな形(表示名なし)。空欄や null の場合は既定のひな形を使う。</summary>
    [JsonIgnore]
    public string EffectiveCallPhraseTemplateWithoutDisplayName => string.IsNullOrWhiteSpace(CallPhraseTemplateWithoutDisplayName)
        ? DefaultCallPhraseTemplateWithoutDisplayName
        : CallPhraseTemplateWithoutDisplayName;

    /// <summary>有効な完了時「ぷるん」の強さ。0以下や大きすぎる値(0.5超)の場合は既定値を使う。</summary>
    [JsonIgnore]
    public double EffectiveCompletionBounceAmplitude => CompletionBounceAmplitude is > 0 and <= 0.5
        ? CompletionBounceAmplitude
        : DefaultCompletionBounceAmplitude;

    /// <summary>有効な完了時「ぷるん」の回数。1〜10回の範囲外なら既定値を使う。</summary>
    [JsonIgnore]
    public int EffectiveCompletionBounceCount => CompletionBounceCount is >= 1 and <= 10
        ? CompletionBounceCount
        : DefaultCompletionBounceCount;

    /// <summary>有効な完了時「ぷるん」1回分の長さ(ミリ秒)。100〜5000の範囲外なら既定値を使う。</summary>
    [JsonIgnore]
    public int EffectiveCompletionBounceDurationMilliseconds => CompletionBounceDurationMilliseconds is >= 100 and <= 5000
        ? CompletionBounceDurationMilliseconds
        : DefaultCompletionBounceDurationMilliseconds;

    /// <summary>有効な作業中の揺れの角度(度)。0以下や大きすぎる値(5度超)の場合は既定値を使う。</summary>
    [JsonIgnore]
    public double EffectiveRunningSwayAngleDegrees => RunningSwayAngleDegrees is > 0 and <= 5
        ? RunningSwayAngleDegrees
        : DefaultRunningSwayAngleDegrees;

    /// <summary>有効な作業中の揺れの1往復の時間(ミリ秒)。500〜20000の範囲外なら既定値を使う。</summary>
    [JsonIgnore]
    public int EffectiveRunningSwayPeriodMilliseconds => RunningSwayPeriodMilliseconds is >= 500 and <= 20000
        ? RunningSwayPeriodMilliseconds
        : DefaultRunningSwayPeriodMilliseconds;

    /// <summary>有効な作業中の伸び縮みの強さ。負の値や大きすぎる値(0.1超)の場合は既定値を使う(0なら伸び縮みしない)。</summary>
    [JsonIgnore]
    public double EffectiveRunningSwayBreathAmplitude => RunningSwayBreathAmplitude is >= 0 and <= 0.1
        ? RunningSwayBreathAmplitude
        : DefaultRunningSwayBreathAmplitude;

    /// <summary>有効な完了後の光の色。#RRGGBB / #AARRGGBB の形式でなければ既定色を使う。</summary>
    [JsonIgnore]
    public string EffectiveCompletionGlowColor => IsHexColor(CompletionGlowColor)
        ? CompletionGlowColor!.Trim()
        : DefaultCompletionGlowColor;

    /// <summary>有効な完了後の光の枠の太さ。1〜10の範囲外なら既定値を使う。</summary>
    [JsonIgnore]
    public double EffectiveCompletionGlowThickness => CompletionGlowThickness is >= 1 and <= 10
        ? CompletionGlowThickness
        : DefaultCompletionGlowThickness;

    /// <summary>有効な完了後の外側の光のぼかし半径。0〜60の範囲外なら既定値を使う(0なら外側の光なし)。</summary>
    [JsonIgnore]
    public double EffectiveCompletionGlowBlurRadius => CompletionGlowBlurRadius is >= 0 and <= 60
        ? CompletionGlowBlurRadius
        : DefaultCompletionGlowBlurRadius;

    /// <summary>有効な完了後の光の明滅周期(ミリ秒)。0は明滅なし。負の値や300未満・20000超は既定値を使う。</summary>
    [JsonIgnore]
    public int EffectiveCompletionGlowPulseMilliseconds => CompletionGlowPulseMilliseconds == 0 || CompletionGlowPulseMilliseconds is >= 300 and <= 20000
        ? CompletionGlowPulseMilliseconds
        : DefaultCompletionGlowPulseMilliseconds;

    /// <summary>
    /// 文字列が「#」に続く6桁または8桁の16進数(#RRGGBB / #AARRGGBB)かどうかを判定する。
    /// </summary>
    /// <param name="value">判定する色の文字列。</param>
    /// <returns>色として使える形式ならtrue。</returns>
    private static bool IsHexColor(string? value)
    {
        var text = value?.Trim();
        if (text is null || text.Length is not (7 or 9) || text[0] != '#')
        {
            return false;
        }

        return text.Skip(1).All(Uri.IsHexDigit);
    }

    /// <summary>有効な一時表示の時間(ミリ秒)。0以下など不正値の場合は既定値を使う。</summary>
    [JsonIgnore]
    public int EffectiveCopyFeedbackDurationMilliseconds => CopyFeedbackDurationMilliseconds > 0
        ? CopyFeedbackDurationMilliseconds
        : DefaultCopyFeedbackDurationMilliseconds;

    /// <summary>有効なClaude CLIコマンド名またはパス。</summary>
    [JsonIgnore]
    public string EffectiveClaudeCliPath => string.IsNullOrWhiteSpace(ClaudeCliPath)
        ? CliPathResolver.Resolve(CliConstants.DefaultClaudeCliName, CliConstants.ClaudeCliFallbackPaths)
        : ClaudeCliPath;

    /// <summary>有効なGemini CLIコマンド名またはパス。</summary>
    [JsonIgnore]
    public string EffectiveGeminiCliPath => string.IsNullOrWhiteSpace(GeminiCliPath)
        ? CliPathResolver.Resolve(CliConstants.DefaultGeminiCliName, CliConstants.GeminiCliFallbackPaths)
        : GeminiCliPath;

    /// <summary>有効なClaudeの直近会話の範囲(分)。0以下なら既定値(5分)。</summary>
    [JsonIgnore]
    public int EffectiveClaudeRecentConversationMinutes => ClaudeRecentConversationMinutes > 0
        ? ClaudeRecentConversationMinutes
        : DefaultClaudeRecentConversationMinutes;

    /// <summary>有効な非同期サブエージェントの最大追跡時間(分)。0以下なら既定値(30分)。</summary>
    [JsonIgnore]
    public int EffectiveAsyncSubagentMaxTrackMinutes => AsyncSubagentMaxTrackMinutes > 0
        ? AsyncSubagentMaxTrackMinutes
        : DefaultAsyncSubagentMaxTrackMinutes;

    /// <summary>有効な診断ログのサイズ上限(バイト)。0以下なら既定値(1MB)。</summary>
    [JsonIgnore]
    public long EffectiveDiagnosticLogMaxBytes => DiagnosticLogMaxBytes > 0
        ? DiagnosticLogMaxBytes
        : DefaultDiagnosticLogMaxBytes;

    /// <summary>有効な作業中の最短表示秒数。負の値なら既定値(3秒)、0は最短表示なし。</summary>
    [JsonIgnore]
    public int EffectiveMinRunningDisplaySeconds => MinRunningDisplaySeconds >= 0
        ? MinRunningDisplaySeconds
        : DefaultMinRunningDisplaySeconds;

    /// <summary>有効なGeminiの直近会話の範囲(分)。0以下なら既定値(5分)。</summary>
    [JsonIgnore]
    public int EffectiveGeminiRecentConversationMinutes => GeminiRecentConversationMinutes > 0
        ? GeminiRecentConversationMinutes
        : DefaultGeminiRecentConversationMinutes;

    /// <summary>設定ファイルが無い場合、または壊れている場合に使う既定値。</summary>
    public static AppSettings Default => new(DefaultSessionRefreshIntervalSeconds, DefaultLogPollIntervalSeconds);
}
