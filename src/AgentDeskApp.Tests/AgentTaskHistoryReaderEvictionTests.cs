using System.Collections;
using System.Reflection;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// イジワルQA: <see cref="AgentTaskHistoryReader"/>のファイルインデックス・エビクション(G-6/C-5)に関するテスト。
/// 実ファイルシステム(%USERPROFILE%\.claude\projects等)には一切触れず、
/// リフレクションでprivate staticな内部辞書を直接操作し、
/// 「走査対象から外れたファイルのインデックスが確実に破棄されメモリリークしないこと」を検証する。
/// </summary>
public class AgentTaskHistoryReaderEvictionTests
{
    [Fact]
    public void EvictStaleClaudeFileIndexes_走査対象外のキーのみ破棄され対象内のキーは残る()
    {
        var readerType = typeof(AgentTaskHistoryReader);
        var indexesField = readerType.GetField("_claudeFileIndexes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("_claudeFileIndexesフィールドが見つかりません(実装変更の可能性)");
        var lockField = readerType.GetField("_claudeIndexLock", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("_claudeIndexLockフィールドが見つかりません(実装変更の可能性)");
        var evictMethod = readerType.GetMethod("EvictStaleClaudeFileIndexes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("EvictStaleClaudeFileIndexesメソッドが見つかりません(実装変更の可能性)");

        var indexNestedType = readerType.GetNestedType("ClaudeFileIndex", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ClaudeFileIndexネスト型が見つかりません(実装変更の可能性)");

        var indexes = (IDictionary)indexesField.GetValue(null)!;
        var lockObj = lockField.GetValue(null)!;

        const string keepPath = @"C:\fake\keep.jsonl";
        const string staleUnicodePath = @"C:\fake\🎉絵文字ファイル.jsonl";

        lock (lockObj)
        {
            indexes.Clear(); // 他テストとの干渉を避けるため既存状態をクリア

            indexes[keepPath] = Activator.CreateInstance(indexNestedType, nonPublic: true)!;
            indexes[staleUnicodePath] = Activator.CreateInstance(indexNestedType, nonPublic: true)!;
            indexes[new string('x', 500) + ".jsonl"] = Activator.CreateInstance(indexNestedType, nonPublic: true)!;

            Assert.Equal(3, indexes.Count);
        }

        var scannedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keepPath };
        evictMethod.Invoke(null, [scannedFilePaths]);

        lock (lockObj)
        {
            Assert.Single(indexes);
            Assert.True(indexes.Contains(keepPath));
            Assert.False(indexes.Contains(staleUnicodePath));

            indexes.Clear(); // 後続テストに影響しないよう後始末
        }
    }

    [Fact]
    public void EvictStaleClaudeFileIndexes_走査対象が空集合なら全件破棄される()
    {
        var readerType = typeof(AgentTaskHistoryReader);
        var indexesField = readerType.GetField("_claudeFileIndexes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var lockField = readerType.GetField("_claudeIndexLock", BindingFlags.NonPublic | BindingFlags.Static)!;
        var evictMethod = readerType.GetMethod("EvictStaleClaudeFileIndexes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var indexNestedType = readerType.GetNestedType("ClaudeFileIndex", BindingFlags.NonPublic)!;

        var indexes = (IDictionary)indexesField.GetValue(null)!;
        var lockObj = lockField.GetValue(null)!;

        lock (lockObj)
        {
            indexes.Clear();
            indexes[@"C:\fake\a.jsonl"] = Activator.CreateInstance(indexNestedType, nonPublic: true)!;
            indexes[@"C:\fake\b.jsonl"] = Activator.CreateInstance(indexNestedType, nonPublic: true)!;
        }

        evictMethod.Invoke(null, [new HashSet<string>(StringComparer.OrdinalIgnoreCase)]);

        lock (lockObj)
        {
            Assert.Empty(indexes);
        }
    }

    [Fact]
    public void EvictStaleGeminiFileIndexes_走査対象外のキーのみ破棄される()
    {
        var readerType = typeof(AgentTaskHistoryReader);
        var indexesField = readerType.GetField("_geminiFileIndexes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("_geminiFileIndexesフィールドが見つかりません(実装変更の可能性)");
        var lockField = readerType.GetField("_geminiIndexLock", BindingFlags.NonPublic | BindingFlags.Static)!;
        var evictMethod = readerType.GetMethod("EvictStaleGeminiFileIndexes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var indexNestedType = readerType.GetNestedType("GeminiFileIndex", BindingFlags.NonPublic)!;

        var indexes = (IDictionary)indexesField.GetValue(null)!;
        var lockObj = lockField.GetValue(null)!;

        const string keepPath = @"C:\fake\gemini-keep\transcript.jsonl";
        const string stalePath = @"C:\fake\gemini-stale\transcript.jsonl";

        lock (lockObj)
        {
            indexes.Clear();
            indexes[keepPath] = Activator.CreateInstance(indexNestedType, nonPublic: true)!;
            indexes[stalePath] = Activator.CreateInstance(indexNestedType, nonPublic: true)!;
        }

        evictMethod.Invoke(null, [new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keepPath }]);

        lock (lockObj)
        {
            Assert.Single(indexes);
            Assert.True(indexes.Contains(keepPath));
            Assert.False(indexes.Contains(stalePath));

            indexes.Clear();
        }
    }
}
