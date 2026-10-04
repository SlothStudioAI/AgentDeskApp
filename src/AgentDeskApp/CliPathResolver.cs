using System.IO;

namespace AgentDeskApp;

/// <summary>
/// CLIパス未設定時に、PATH解決→標準インストール場所の順で実在するCLIのフルパスを解決する(U-26)。
/// 存在確認・環境変数取得はデリゲート/引数で差し替え可能にし、単体テストで実ファイルシステムに依存しない。
/// </summary>
public static class CliPathResolver
{
    /// <summary>
    /// 既定名がPATH上で解決できればそのまま既定名を返す。解決できなければ候補パスを順に探し、
    /// 実在した最初のフルパスを返す。どれも見つからなければ既定名を返す(従来どおり未接続扱いになる)。
    /// </summary>
    /// <param name="defaultName">CLIの既定コマンド名(例: "claude")。</param>
    /// <param name="fallbackPaths">標準インストール場所の候補(環境変数は%NAME%形式で可)。</param>
    /// <param name="fileExists">ファイル存在確認。nullなら<see cref="File.Exists"/>。</param>
    /// <param name="pathEnv">PATH環境変数の値。nullなら現在のプロセスの値。</param>
    /// <param name="pathExt">PATHEXT環境変数の値。nullなら現在のプロセスの値(無ければ既定拡張子)。</param>
    public static string Resolve(
        string defaultName,
        IEnumerable<string> fallbackPaths,
        Func<string, bool>? fileExists = null,
        string? pathEnv = null,
        string? pathExt = null)
    {
        fileExists ??= File.Exists;
        pathEnv ??= Environment.GetEnvironmentVariable("PATH");
        pathExt ??= Environment.GetEnvironmentVariable("PATHEXT");

        if (ExistsOnPath(defaultName, fileExists, pathEnv, pathExt))
        {
            return defaultName;
        }

        foreach (var candidate in fallbackPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            // 未定義の環境変数が残っている候補は無効として飛ばす
            if (expanded.Contains('%'))
            {
                continue;
            }

            if (fileExists(expanded))
            {
                return expanded;
            }
        }

        return defaultName;
    }

    /// <summary>PATH内の各ディレクトリに、既定名(PATHEXTの各拡張子付き含む)のファイルが存在するかを返す。</summary>
    /// <param name="name">コマンド名。</param>
    /// <param name="fileExists">ファイル存在確認。</param>
    /// <param name="pathEnv">PATH環境変数の値。</param>
    /// <param name="pathExt">PATHEXT環境変数の値。</param>
    private static bool ExistsOnPath(string name, Func<string, bool> fileExists, string? pathEnv, string? pathExt)
    {
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return false;
        }

        var extensions = (string.IsNullOrWhiteSpace(pathExt) ? ".COM;.EXE;.BAT;.CMD" : pathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var ext in extensions)
            {
                if (fileExists(Path.Combine(trimmed, name + ext)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
