namespace AgentDeskApp;

/// <summary>
/// Claude/Gemini CLIの既定コマンド名やバージョン判定文字列などを共通化する定数クラス(C-7)。
/// <see cref="AppSettings"/>の既定値や<see cref="CliAvailability"/>のバージョン検出処理が
/// 個別にハードコードしていたCLI名・引数文字列をここに集約する。
/// </summary>
public static class CliConstants
{
    /// <summary>Claude CLIの既定コマンド名。</summary>
    public const string DefaultClaudeCliName = "claude";

    /// <summary>Gemini(Antigravity) CLIの既定コマンド名。</summary>
    public const string DefaultGeminiCliName = "agy";

    /// <summary>
    /// Claude CLIの標準インストール場所の候補(U-26)。先頭から順に探索する。
    /// 環境変数(%APPDATA%等)は展開前の形式で持ち、<see cref="CliPathResolver"/>が展開する。
    /// </summary>
    public static readonly string[] ClaudeCliFallbackPaths =
    {
        @"%APPDATA%\npm\claude.cmd",
        @"%USERPROFILE%\.local\bin\claude.exe",
    };

    /// <summary>Gemini(Antigravity) CLIの標準インストール場所の候補(U-26)。</summary>
    public static readonly string[] GeminiCliFallbackPaths =
    {
        @"%USERPROFILE%\.gemini\bin\agy.exe",
    };

    /// <summary>CLIの可用性・バージョン検出に使う引数。</summary>
    public const string VersionArgument = "--version";
}
