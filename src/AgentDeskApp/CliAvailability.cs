using System.Diagnostics;
using System.IO;

namespace AgentDeskApp;

/// <summary>
/// Claude/Gemini CLIが実際に実行可能かどうかを検出する共通ヘルパー(U-11, U-24)。
/// SettingsWindowの「✅ 検出済み」表示と、MainWindowの「⚠️ 未接続」バッジ・実行ブロックの両方から使う。
/// </summary>
public static class CliAvailability
{
    /// <summary>cmd.exe経由で "{cliPath} --version" を実行し、成功すればバージョン文字列を返す。失敗時はnull。</summary>
    public static async Task<string?> DetectVersionAsync(string cliPath)
    {
        cliPath = cliPath.Trim();
        if (cliPath.Length == 0)
        {
            return null;
        }

        try
        {
            var psi = CreateVersionStartInfo(cliPath);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return null;
            }

            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var exited = await Task.Run(() => proc.WaitForExit(5000));
            if (!exited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var output = (await outputTask).Trim();
            return proc.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// "{cliPath} --version" を実行するための<see cref="ProcessStartInfo"/>(FileNameとArgumentsのみ設定)を組み立てる (BUG-5)。
    /// 実在する.exe/.comへのパスならcmd.exeを介さず直接起動し、スペースや&amp;・()などを含むパスでも壊れないようにする。
    /// それ以外(PATH上のコマンド名や.cmd/.batのnpmシムなど)はcmd.exe経由にするが、コマンド部分だけを引用符で囲む
    /// (<c>/c ""パス" 引数"</c>形式。全体を1組の引用符で囲むとスペース入りパスがcmdに分割されてしまうため)。
    /// </summary>
    /// <param name="cliPath">CLIのコマンド名またはパス。</param>
    public static ProcessStartInfo CreateVersionStartInfo(string cliPath) =>
        CreateCliCommandStartInfo(cliPath, CliConstants.VersionArgument);

    /// <summary>
    /// "{cliPath} {arguments}" を実行するための<see cref="ProcessStartInfo"/>(FileNameとArgumentsのみ設定)を
    /// 汎用的に組み立てる(Q-2)。実在する.exe/.comへのパスならcmd.exeを介さず直接起動し、スペースや&amp;・()
    /// などを含むパスでも壊れないようにする。それ以外(PATH上のコマンド名や.cmd/.batのnpmシムなど)は
    /// cmd.exe経由にするが、コマンド部分だけを引用符で囲む(<c>/c ""パス" 引数"</c>形式。全体を1組の引用符で
    /// 囲むとスペース入りパスがcmdに分割されてしまうため)。<see cref="CreateVersionStartInfo"/>や
    /// <see cref="MemberActivityMonitor.RunAgentsJsonCommandAsync"/>など、CLIコマンドを起動する箇所は
    /// すべてこのメソッドに統一し、引用符処理の不整合を防ぐ。
    /// </summary>
    /// <param name="cliPath">CLIのコマンド名またはパス。</param>
    /// <param name="arguments">CLIに渡す引数(例: "--version", "agents --json")。</param>
    public static ProcessStartInfo CreateCliCommandStartInfo(string cliPath, string arguments)
    {
        var path = cliPath.Trim().Trim('"');

        var extension = Path.GetExtension(path);
        if (File.Exists(path) &&
            (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".com", StringComparison.OrdinalIgnoreCase)))
        {
            return new ProcessStartInfo { FileName = path, Arguments = arguments };
        }

        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{path}\" {arguments}\"",
        };
    }

    /// <summary>指定したCLIコマンド/パスが実行可能かどうかを返す。</summary>
    public static async Task<bool> IsAvailableAsync(string cliPath) => await DetectVersionAsync(cliPath) != null;
}
