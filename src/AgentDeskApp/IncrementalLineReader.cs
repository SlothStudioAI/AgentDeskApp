using System.IO;
using System.Text;

namespace AgentDeskApp;

/// <summary>
/// テキストログファイル(主にJSONL形式)の増分読み込みを共通化するリーダー(G-6/C-5)。
/// 前回読み取ったバイトオフセットを内部に保持し、呼び出しの都度ファイル全体を読み直さず
/// 追記された差分のみを読み取ることで、セッションファイル肥大化時の走査コストを抑える。
/// </summary>
public sealed class IncrementalLineReader
{
    private long _offset;

    /// <summary>現在保持している読み取り済みバイトオフセット。</summary>
    public long Offset => _offset;

    /// <summary>
    /// 直前の<see cref="ReadNewLines"/>呼び出しで、ファイルサイズが前回より縮小していた
    /// (ローテーションや再作成等)ことを検知し、オフセットを先頭にリセットしたかどうか。
    /// 呼び出し元は、このフラグが立っていた場合、蓄積していた解析済み状態を破棄すべきである。
    /// </summary>
    public bool WasTruncated { get; private set; }

    /// <summary>
    /// 指定ファイルの前回読み取り位置からの追記分を読み取り、改行(0x0A)で完結している
    /// 行だけを文字列リストとして返す。書き込み途中で改行に到達していない末尾の断片は
    /// 読み進めず、次回呼び出し時にまとめて読み取り直す。
    /// ファイルが存在しない場合は状態を変更せず空リストを返す。
    /// </summary>
    /// <param name="filePath">読み取り対象ファイルの絶対パス。</param>
    public List<string> ReadNewLines(string filePath)
    {
        WasTruncated = false;
        var lines = new List<string>();

        if (!File.Exists(filePath))
        {
            return lines;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (_offset > stream.Length)
        {
            // ファイルが前回より短くなっている(ローテーション・作り直し等)ため先頭から読み直す。
            _offset = 0;
            WasTruncated = true;
        }

        var remaining = stream.Length - _offset;
        if (remaining <= 0)
        {
            return lines;
        }

        var isReadingFromFileStart = _offset == 0;

        stream.Seek(_offset, SeekOrigin.Begin);
        var buffer = new byte[remaining];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);
        if (bytesRead <= 0)
        {
            return lines;
        }

        // 末尾が改行で終わっていない(書き込み途中の行)可能性があるため、最後の改行までしか処理しない。
        var lastNewlineIndex = Array.LastIndexOf(buffer, (byte)'\n', bytesRead - 1);
        if (lastNewlineIndex < 0)
        {
            return lines;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, lastNewlineIndex + 1);
        _offset += lastNewlineIndex + 1;

        // ファイル先頭(オフセット0)から読んだ場合、UTF-8 BOM(EF BB BF → ﻿)が
        // デコード後の文字列先頭に残ることがあるため、1行目からのみトリム除去する。
        if (isReadingFromFileStart && text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>
    /// 読み取り位置を現在のファイル末尾へ進める(初回発見前の過去の行を処理しないため)。
    /// ファイルが無い場合は何もしない(その場合は以後、新規作成された内容を先頭から読む)。
    /// </summary>
    /// <param name="filePath">対象ファイルの絶対パス。</param>
    public void SkipToEnd(string filePath)
    {
        WasTruncated = false;
        if (!File.Exists(filePath))
        {
            return;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _offset = stream.Length;
    }

    /// <summary>内部の読み取り位置を先頭に戻す(ファイルの再走査が必要な場合に使用)。</summary>
    public void Reset()
    {
        _offset = 0;
        WasTruncated = false;
    }
}
