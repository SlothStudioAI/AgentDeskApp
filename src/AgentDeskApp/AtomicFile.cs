using System.IO;
using System.Text;

namespace AgentDeskApp;

/// <summary>
/// 設定・定義・ルールファイルをアトミックに書き込むヘルパー (BUG-14)。
/// 同じフォルダに一時ファイルを書き出してから置換するため、書き込み途中でクラッシュ・強制終了しても
/// 元のファイルが0バイトや中途半端な内容に壊れることがない。
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// テキストをアトミックに書き込む。既存ファイルがあれば置き換え、無ければ新規作成する。
    /// </summary>
    /// <param name="path">書き込み先の絶対パス。</param>
    /// <param name="content">書き込む文字列。</param>
    /// <param name="encoding">エンコーディング(省略時はBOM無しUTF-8。<see cref="File.WriteAllText(string, string?)"/>と同じ)。</param>
    public static void WriteAllText(string path, string content, Encoding? encoding = null)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // 失敗時は一時ファイルを残さない(元ファイルには一切触れていない)
            try { File.Delete(tempPath); } catch (IOException) { /* 後始末の失敗は無視 */ }
            throw;
        }
    }

    /// <summary>
    /// 行の一覧をアトミックに書き込む(<see cref="File.WriteAllLines(string, IEnumerable{string})"/>相当。各行の末尾に改行が付く)。
    /// </summary>
    /// <param name="path">書き込み先の絶対パス。</param>
    /// <param name="lines">書き込む行。</param>
    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line).Append(Environment.NewLine);
        }

        WriteAllText(path, builder.ToString());
    }
}
