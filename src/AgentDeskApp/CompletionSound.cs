using System.IO;
using System.Media;

namespace AgentDeskApp;

/// <summary>
/// サブエージェント完了時の通知音を鳴らすクラス。設定で音ファイル(WAV)が指定されていればそれを、
/// 未指定・存在しない・読めない場合はWindows標準の通知音(SystemSounds.Asterisk)を鳴らす。
/// 音が鳴らせなくてもアプリが落ちないよう、例外はすべて握りつぶす。
/// </summary>
public static class CompletionSound
{
    /// <summary>
    /// 再生中のプレイヤー。非同期再生中に音声データがGCで回収されないよう、次の再生まで参照を保持する。
    /// </summary>
    private static SoundPlayer? _currentPlayer;

    /// <summary>
    /// 設定値の音ファイルパスを、実際に再生を試みるファイルパスへ解決する。
    /// </summary>
    /// <param name="configuredPath">設定ファイルに書かれた音ファイルパス(未指定ならnull)。</param>
    /// <returns>存在するファイルならそのフルパス。未指定・存在しない場合はnull(=標準の通知音を使う)。</returns>
    public static string? ResolveSoundFile(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"')));
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// 完了の通知音を鳴らす。指定ファイルの再生に失敗した場合はWindows標準の通知音にフォールバックする。
    /// </summary>
    /// <param name="configuredPath">設定ファイルに書かれた音ファイルパス(未指定ならnull)。</param>
    public static void Play(string? configuredPath)
    {
        var soundFile = ResolveSoundFile(configuredPath);
        if (soundFile is not null)
        {
            try
            {
                // Play()は非同期再生(読み込みは同期)。WAV以外・壊れたファイルは例外になるので標準音へ切り替える
                var player = new SoundPlayer(soundFile);
                _currentPlayer?.Stop();
                player.Play();
                _currentPlayer = player;
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentDesk] 通知音ファイルの再生に失敗(標準音で代替): {ex.Message}");
            }
        }

        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] 標準通知音の再生に失敗: {ex.Message}");
        }
    }
}
