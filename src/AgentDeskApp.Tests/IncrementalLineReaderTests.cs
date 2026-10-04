using System.Text;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="IncrementalLineReader"/>(ログファイルの増分読み込み共通処理、G-6/C-5)に関するテスト。
/// </summary>
public class IncrementalLineReaderTests
{
    [Fact]
    public void ReadNewLines_初回は先頭から全行を読み取る()
    {
        var path = CreateTempFile("line1\nline2\n");
        try
        {
            var reader = new IncrementalLineReader();
            var lines = reader.ReadNewLines(path);

            Assert.Equal(["line1", "line2"], lines);
            Assert.False(reader.WasTruncated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_追記分のみを次回読み取る()
    {
        var path = CreateTempFile("line1\n");
        try
        {
            var reader = new IncrementalLineReader();
            var first = reader.ReadNewLines(path);
            Assert.Equal(["line1"], first);

            File.AppendAllText(path, "line2\nline3\n");
            var second = reader.ReadNewLines(path);

            Assert.Equal(["line2", "line3"], second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_変化がなければ空リストを返す()
    {
        var path = CreateTempFile("line1\n");
        try
        {
            var reader = new IncrementalLineReader();
            reader.ReadNewLines(path);

            var second = reader.ReadNewLines(path);

            Assert.Empty(second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_改行で終わっていない末尾データは保留し次回まで読まない()
    {
        var path = CreateTempFile("line1\nline2"); // line2は改行なし(書き込み途中)
        try
        {
            var reader = new IncrementalLineReader();
            var first = reader.ReadNewLines(path);

            Assert.Equal(["line1"], first);

            File.AppendAllText(path, "\n");
            var second = reader.ReadNewLines(path);

            Assert.Equal(["line2"], second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_ファイルサイズ縮小時はリセットしWasTruncatedが立つ()
    {
        var path = CreateTempFile("line1\nline2\nline3\n");
        try
        {
            var reader = new IncrementalLineReader();
            reader.ReadNewLines(path);

            // ファイルが縮小(再作成)されたことをシミュレート
            File.WriteAllText(path, "new1\n");
            var afterTruncate = reader.ReadNewLines(path);

            Assert.True(reader.WasTruncated);
            Assert.Equal(["new1"], afterTruncate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_ファイルが存在しなければ空リストで状態も変更しない()
    {
        var reader = new IncrementalLineReader();
        var missingPath = Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid() + ".jsonl");

        var lines = reader.ReadNewLines(missingPath);

        Assert.Empty(lines);
        Assert.False(reader.WasTruncated);
        Assert.Equal(0, reader.Offset);
    }

    [Fact]
    public void ReadNewLines_空行はスキップされる()
    {
        var path = CreateTempFile("line1\n\nline2\n");
        try
        {
            var reader = new IncrementalLineReader();
            var lines = reader.ReadNewLines(path);

            Assert.Equal(["line1", "line2"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_先頭のUTF8BOMは1行目からトリム除去される()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var path = Path.Combine(Path.GetTempPath(), "incremental-reader-test-" + Guid.NewGuid() + ".jsonl");
        File.WriteAllBytes(path, [.. bom, .. Encoding.UTF8.GetBytes("line1\nline2\n")]);
        try
        {
            var reader = new IncrementalLineReader();
            var lines = reader.ReadNewLines(path);

            Assert.Equal(["line1", "line2"], lines);
            Assert.DoesNotContain('﻿', lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reset_呼び出すとオフセットが先頭に戻る()
    {
        var path = CreateTempFile("line1\n");
        try
        {
            var reader = new IncrementalLineReader();
            reader.ReadNewLines(path);
            Assert.True(reader.Offset > 0);

            reader.Reset();

            Assert.Equal(0, reader.Offset);
            var again = reader.ReadNewLines(path);
            Assert.Equal(["line1"], again);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ==== ここからイジワルQA: 異常系・境界値の追加テスト ====

    [Fact]
    public void ReadNewLines_改行なし断片が複数回の追記に分割されても最終的に1行として結合される()
    {
        // 1バイトずつ・改行なしで3回に分けて書き込まれるケース(遅い書き込みプロセスを想定)。
        var path = CreateTempFile("");
        try
        {
            var reader = new IncrementalLineReader();

            File.AppendAllText(path, "par");
            Assert.Empty(reader.ReadNewLines(path)); // 改行未到達のため何も読まれない

            File.AppendAllText(path, "tial-l");
            Assert.Empty(reader.ReadNewLines(path)); // まだ改行なし

            File.AppendAllText(path, "ine\n");
            var result = reader.ReadNewLines(path);

            Assert.Equal(["partial-line"], result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_縮小後に旧オフセットを超える追記があっても新データのみ正しく読まれる()
    {
        var path = CreateTempFile("line1\nline2\nline3\nline4\nline5\n"); // 30バイト相当
        try
        {
            var reader = new IncrementalLineReader();
            reader.ReadNewLines(path);
            var offsetBeforeTruncate = reader.Offset;

            // 縮小(再作成)して短い内容に置き換え
            File.WriteAllText(path, "new1\n");
            var afterTruncate = reader.ReadNewLines(path);
            Assert.True(reader.WasTruncated);
            Assert.Equal(["new1"], afterTruncate);
            Assert.True(reader.Offset < offsetBeforeTruncate);

            // さらに追記して旧オフセットを超えるサイズまで増やす
            var sb = new StringBuilder();
            for (var i = 0; i < 10; i++)
            {
                sb.Append("extra-line-").Append(i).Append('\n');
            }
            File.AppendAllText(path, sb.ToString());

            var afterGrow = reader.ReadNewLines(path);

            Assert.False(reader.WasTruncated); // 今回は縮小していない
            Assert.Equal(10, afterGrow.Count);
            Assert.Equal("extra-line-0", afterGrow[0]);
            Assert.Equal("extra-line-9", afterGrow[9]);
            // "new1"が重複して再読み込みされていないこと
            Assert.DoesNotContain("new1", afterGrow);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_サイズが同一以上の書き換えは縮小として検出されない_既知の制限()
    {
        // WasTruncatedはファイルサイズが「前回より小さくなった」場合のみ検知する。
        // 同一サイズ・別内容への差し替え(atomic rename等)は検知できない実装上の制限を明示する。
        var path = CreateTempFile("aaaa\nbbbb\n"); // 10バイト
        try
        {
            var reader = new IncrementalLineReader();
            reader.ReadNewLines(path);

            // 同じバイト数で全く別内容に差し替え
            File.WriteAllText(path, "cccc\ndddd\n");
            var result = reader.ReadNewLines(path);

            Assert.False(reader.WasTruncated);
            Assert.Empty(result); // オフセットが末尾のままのため新規行として読まれない(既知の限界)
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_絵文字4バイトUTF8が改行直前にあってもデコード破損しない()
    {
        var path = CreateTempFile("");
        try
        {
            var reader = new IncrementalLineReader();
            // 🎉 (U+1F389) はUTF-8で4バイト、サロゲートペアとしてUTF-16表現される。
            File.WriteAllText(path, "hello🎉\nworld\n", new UTF8Encoding(false));

            var lines = reader.ReadNewLines(path);

            Assert.Equal(["hello🎉", "world"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_絵文字が不完全行の断片として保留された後も次回追記で正しく結合される()
    {
        var path = CreateTempFile("");
        try
        {
            var reader = new IncrementalLineReader();

            // 絵文字を含む行を改行なしで書き込み、いったん保留させる
            File.AppendAllText(path, "emoji-test-🚀-part", new UTF8Encoding(false));
            Assert.Empty(reader.ReadNewLines(path));

            // 続きと改行を追記
            File.AppendAllText(path, "-two\n", new UTF8Encoding(false));
            var lines = reader.ReadNewLines(path);

            Assert.Equal(["emoji-test-🚀-part-two"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_空ファイルは空リストを返しオフセットも変化しない()
    {
        var path = CreateTempFile("");
        try
        {
            var reader = new IncrementalLineReader();
            var lines = reader.ReadNewLines(path);

            Assert.Empty(lines);
            Assert.Equal(0, reader.Offset);
            Assert.False(reader.WasTruncated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_改行のみの行が連続してもクラッシュせず空行は除去される()
    {
        var path = CreateTempFile("\n\n\n\nline1\n\n\n");
        try
        {
            var reader = new IncrementalLineReader();
            var lines = reader.ReadNewLines(path);

            Assert.Equal(["line1"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_CRLF改行でも正しく行末のCRが除去される()
    {
        var path = CreateTempFile("line1\r\nline2\r\n");
        try
        {
            var reader = new IncrementalLineReader();
            var lines = reader.ReadNewLines(path);

            Assert.Equal(["line1", "line2"], lines);
            Assert.DoesNotContain(lines, l => l.Contains('\r'));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNewLines_非常に長い1行でもメモリ確保でクラッシュせず読み取れる()
    {
        // 数MB相当の1行を書き込んでも例外にならないことを確認する(極端な長文プロンプト等を想定)。
        var longLine = new string('x', 5_000_000);
        var path = CreateTempFile(longLine + "\n");
        try
        {
            var reader = new IncrementalLineReader();
            var lines = reader.ReadNewLines(path);

            Assert.Single(lines);
            Assert.Equal(longLine.Length, lines[0].Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "incremental-reader-test-" + Guid.NewGuid() + ".jsonl");
        File.WriteAllText(path, content);
        return path;
    }
}
