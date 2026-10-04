using System.Diagnostics;
using System.IO;
using System.Text;

namespace AgentDeskApp;

/// <summary>
/// 診断ログ(作業中・完了の検知の切り分け用)をファイルへ追記するクラス。
/// 1行ごとにローカル時刻(ミリ秒まで)を付け、サイズ上限に達したら「.1」へ退避(最大2ファイル)する。
/// 個人情報(プロンプト本文・ファイルパス全文・会話内容)は呼び出し側が渡さない前提。
/// 書き込みの失敗は握りつぶし、アプリの動作に影響させない。lockで直列化した同期書き込み。
/// </summary>
public sealed class DiagnosticLog
{
    /// <summary>ログファイルの既定のファイル名。</summary>
    public const string DefaultFileName = "agentdesk-diagnostic.log";

    /// <summary>退避ファイルの拡張子(元のパスに付け足す)。</summary>
    public const string RotatedSuffix = ".1";

    /// <summary>アプリ全体で共有する診断ログ。起動時にMainWindowが設定(有効・上限)を反映する。既定は無効。</summary>
    public static DiagnosticLog Shared { get; } = new(DefaultPath, enabled: false, AppSettings.DefaultDiagnosticLogMaxBytes);

    /// <summary>既定の出力先(%APPDATA%\AgentDeskApp\logs\agentdesk-diagnostic.log。settings.jsonと同じAgentDeskAppフォルダ配下)。</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentDeskApp", "logs", DefaultFileName);

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _lastByKey = [];
    private long _currentSize = -1;

    /// <summary>出力先ファイルの絶対パス。</summary>
    public string FilePath { get; }

    /// <summary>ファイルへ書くかどうか(falseなら何も書かない)。</summary>
    public bool Enabled { get; set; }

    /// <summary>1ファイルのサイズ上限(バイト)。超えると退避する。0以下なら既定値を使う。</summary>
    public long MaxBytes { get; set; }

    /// <summary>行頭の時刻に使う現在時刻の取得関数(テストで差し替える)。既定はローカル時刻。</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>
    /// 診断ログを作る。
    /// </summary>
    /// <param name="path">出力先ファイルの絶対パス。</param>
    /// <param name="enabled">ファイルへ書くか。</param>
    /// <param name="maxBytes">1ファイルのサイズ上限(バイト)。</param>
    public DiagnosticLog(string path, bool enabled, long maxBytes)
    {
        FilePath = path;
        Enabled = enabled;
        MaxBytes = maxBytes;
    }

    /// <summary>
    /// 1行を書き出す。ファイルが有効なときは追記し、常にDebug出力にも出す(先頭 [AgentDesk])。
    /// 書き込みに失敗しても例外を出さない。
    /// </summary>
    /// <param name="message">1行分のメッセージ(個人情報を含めないこと)。</param>
    public void Write(string message)
    {
        Debug.WriteLine($"[AgentDesk] {message}");

        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                var line = $"{Clock():yyyy-MM-dd HH:mm:ss.fff} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}";
                var bytes = Encoding.UTF8.GetBytes(line);
                var limit = MaxBytes > 0 ? MaxBytes : AppSettings.DefaultDiagnosticLogMaxBytes;

                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (_currentSize < 0)
                {
                    _currentSize = File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
                }

                if (_currentSize > 0 && _currentSize + bytes.Length > limit)
                {
                    Rotate();
                }

                using (var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                _currentSize += bytes.Length;
            }
        }
        catch (Exception)
        {
            // 診断ログの失敗でアプリの動作を止めない。次回の書き込み時にサイズを取り直す。
            _currentSize = -1;
        }
    }

    /// <summary>
    /// 同じキーの前回メッセージと違うときだけ書き出す(周期ごとに同じ内容が並ぶのを防ぐ)。
    /// </summary>
    /// <param name="key">変化を比べる単位(例: "agents")。</param>
    /// <param name="message">1行分のメッセージ。</param>
    public void WriteOnChange(string key, string message)
    {
        lock (_gate)
        {
            if (_lastByKey.TryGetValue(key, out var last) && last == message)
            {
                return;
            }

            _lastByKey[key] = message;
        }

        Write(message);
    }

    /// <summary>現在のファイルを「.1」へ退避する(既存の.1は置き換える)。ロック取得済みで呼ぶこと。</summary>
    private void Rotate()
    {
        var rotated = FilePath + RotatedSuffix;
        if (File.Exists(rotated))
        {
            File.Delete(rotated);
        }

        File.Move(FilePath, rotated);
        _currentSize = 0;
    }
}
