using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentDeskApp;

/// <summary>
/// 1セッションの会話ログファイル(.jsonl)を増分読み込みし、直近に読み書き・検索したファイルの
/// 絶対パスを追跡するクラス(design.md §9.4)。リーダーカードの「作業」欄(今どのチームを
/// 触っているか)を推定するために、Read/Edit/Write/NotebookEdit/Glob/Grepのtool_useを見る。
/// <see cref="SessionSubagentTracker"/>と同じく、前回読んだバイト位置を覚えて追記分だけ読む。
/// </summary>
public sealed class SessionWorkspaceTracker
{
    private static readonly HashSet<string> FilePathToolNames = ["Read", "Edit", "Write", "NotebookEdit"];
    private static readonly HashSet<string> SearchPathToolNames = ["Glob", "Grep"];

    private long _readOffset;

    /// <summary>直近にRead/Edit/Write/Glob/Grep等で触った絶対パス。まだ何も見ていなければnull。</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>会話ログファイルの新規追記分を読み込み、<see cref="CurrentPath"/>を更新する。</summary>
    /// <param name="transcriptFilePath">会話ログファイル(.jsonl)の絶対パス。</param>
    public void Poll(string transcriptFilePath)
    {
        if (!File.Exists(transcriptFilePath))
        {
            return;
        }

        using var stream = new FileStream(transcriptFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (_readOffset > stream.Length)
        {
            _readOffset = 0;
        }

        var remaining = stream.Length - _readOffset;
        if (remaining <= 0)
        {
            return;
        }

        stream.Seek(_readOffset, SeekOrigin.Begin);
        var buffer = new byte[remaining];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        var lastNewlineIndex = Array.LastIndexOf(buffer, (byte)'\n', bytesRead - 1);
        if (lastNewlineIndex < 0)
        {
            return;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, lastNewlineIndex + 1);
        _readOffset += lastNewlineIndex + 1;

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            ProcessLine(line);
        }
    }

    private void ProcessLine(string line)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "tool_use" ||
                    !item.TryGetProperty("name", out var nameProp) ||
                    !item.TryGetProperty("input", out var inputProp))
                {
                    continue;
                }

                var toolName = nameProp.GetString() ?? string.Empty;

                if (FilePathToolNames.Contains(toolName) && inputProp.TryGetProperty("file_path", out var filePathProp))
                {
                    UpdateCurrentPath(filePathProp.GetString());
                }
                else if (SearchPathToolNames.Contains(toolName) && inputProp.TryGetProperty("path", out var pathProp))
                {
                    UpdateCurrentPath(pathProp.GetString());
                }
            }
        }
    }

    private void UpdateCurrentPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            CurrentPath = path;
        }
    }
}
