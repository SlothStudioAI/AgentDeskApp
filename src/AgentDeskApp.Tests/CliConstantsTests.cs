using System.Reflection;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="CliConstants"/>に定義された探索パス定数の健全性を検証するテスト(B-1)。
/// 過去に通常の文字列リテラル内でバックスラッシュがエスケープシーケンスとして解釈され、
/// パス定数へ制御文字(\n, \b, \a等)が意図せず埋め込まれてしまった不具合の再発防止用。
/// </summary>
public class CliConstantsTests
{
    /// <summary>
    /// <see cref="CliConstants.ClaudeCliFallbackPaths"/>および<see cref="CliConstants.GeminiCliFallbackPaths"/>の
    /// 各要素に、ASCII 32未満の制御文字(タブ・改行・バックスペース・ベル等)が含まれていないことを検証する。
    /// PATHが通っていないまっさらなPCでもフォールバック探索パスが正しく機能することを保証する。
    /// </summary>
    [Fact]
    public void フォールバック探索パスに制御文字が含まれていない()
    {
        var allPaths = CliConstants.ClaudeCliFallbackPaths
            .Concat(CliConstants.GeminiCliFallbackPaths);

        foreach (var path in allPaths)
        {
            foreach (var ch in path)
            {
                Assert.True(ch >= 32, $"制御文字(コード{(int)ch})がパス定数に含まれています: \"{path}\"");
            }
        }
    }

    /// <summary>
    /// <see cref="CliConstants"/>クラス内の全<c>public</c>文字列・文字列配列定数を
    /// リフレクションで網羅的に走査し、将来定数が追加された場合でも同様に制御文字混入を検知できるようにする。
    /// </summary>
    [Fact]
    public void CliConstants内の全ての文字列定数に制御文字が含まれていない()
    {
        var fields = typeof(CliConstants).GetFields(BindingFlags.Public | BindingFlags.Static);

        foreach (var field in fields)
        {
            var value = field.GetValue(null);

            if (value is string s)
            {
                AssertNoControlChars(field.Name, s);
            }
            else if (value is string[] arr)
            {
                foreach (var s2 in arr)
                {
                    AssertNoControlChars(field.Name, s2);
                }
            }
        }
    }

    /// <summary>対象文字列にASCII 32未満の制御文字が含まれていないことを検証する共通ヘルパー。</summary>
    /// <param name="fieldName">検証対象のフィールド名(失敗メッセージ表示用)。</param>
    /// <param name="value">検証対象の文字列。</param>
    private static void AssertNoControlChars(string fieldName, string value)
    {
        foreach (var ch in value)
        {
            Assert.True(ch >= 32, $"制御文字(コード{(int)ch})が{fieldName}に含まれています: \"{value}\"");
        }
    }
}
