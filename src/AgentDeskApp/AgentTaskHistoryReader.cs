using System.IO;
using System.Text.Json;

namespace AgentDeskApp;

/// <summary>
/// 1件のエージェント実行タスク履歴レコード。
/// </summary>
/// <param name="SessionId">セッションID。</param>
/// <param name="Engine">実行エンジン("Claude" または "Gemini")。</param>
/// <param name="Timestamp">実行日時。</param>
/// <param name="Instruction">指示内容(プロンプト)。</param>
/// <param name="Status">実行ステータス("Completed", "Running", "Failed")。</param>
/// <param name="ResultSummary">実行結果・成果物のサマリー。</param>
/// <param name="UserRequest">ユーザーの元の依頼テキスト(抽出できた場合)。</param>
public sealed record AgentTaskRecord(
    string SessionId,
    string Engine,
    DateTime Timestamp,
    string Instruction,
    string Status,
    string? ResultSummary,
    string? UserRequest = null);

/// <summary>
/// Claude Code および Gemini (Antigravity) の過去ログから特定エージェントのタスク実行履歴を収集するクラス。
/// </summary>
public static class AgentTaskHistoryReader
{
    /// <summary>
    /// ログ走査結果のメモリキャッシュ(C-4+)。エージェント名+対象ディレクトリをキーとし、
    /// <see cref="AppSettings.TaskHistoryCacheSeconds"/>の間はディスク走査を省略する。
    /// </summary>
    private static readonly Dictionary<string, (DateTime CachedAtUtc, List<AgentTaskRecord> Records)> _cache = new();
    private static readonly object _cacheLock = new();

    /// <summary>Claudeの1件のAgent/Task呼び出しイベント(agentNameに依存しない生イベント、G-6/C-5)。</summary>
    private sealed record ClaudeAgentInvocation(string ToolUseId, string TargetAgentName, string Prompt, DateTime Timestamp);

    /// <summary>Claudeの1件のtoolUseResultイベント(agentNameに依存しない生イベント、G-6/C-5)。</summary>
    private sealed record ClaudeToolResultEvent(string? ResultToolUseId, string? TurPrompt, string? TurAgentType, string? ResultText, DateTime Timestamp);

    /// <summary>Claudeログ1ファイル分の増分走査済み生イベントインデックス(G-6/C-5)。
    /// ファイルの物理読み込み・JSONパースをagentNameによらず1回だけ行い、
    /// エージェント別の抽出はこのインデックスに対するフィルタとして行う。</summary>
    private sealed class ClaudeFileIndex
    {
        public IncrementalLineReader Reader { get; } = new();
        public string? UserOriginalRequestRawLine;
        public List<ClaudeAgentInvocation> Invocations { get; } = [];
        public List<ClaudeToolResultEvent> ToolResults { get; } = [];
        public List<string> AssistantTexts { get; } = [];
    }

    private static readonly Dictionary<string, ClaudeFileIndex> _claudeFileIndexes = new();
    private static readonly object _claudeIndexLock = new();

    /// <summary>Geminiの1件のinvoke_subagent呼び出しイベント(agentNameに依存しない生イベント、G-6/C-5)。</summary>
    private sealed record GeminiSubagentInvocation(string TypeName, string? Role, string Prompt, DateTime Timestamp);

    /// <summary>Geminiログ1ファイル分の増分走査済み生イベントインデックス(G-6/C-5)。</summary>
    private sealed class GeminiFileIndex
    {
        public IncrementalLineReader Reader { get; } = new();
        public string? UserOriginalRequestRawLine;
        public List<GeminiSubagentInvocation> Invocations { get; } = [];
        public string? LastModelResponse;
    }

    private static readonly Dictionary<string, GeminiFileIndex> _geminiFileIndexes = new();
    private static readonly object _geminiIndexLock = new();

    /// <summary>
    /// 指定されたエージェントの過去タスク履歴を走査して新しい順に取得する。
    /// 結果は件数上限(<see cref="AppSettings.MaxTaskHistoryRecords"/>)で切り詰め、
    /// 一定時間(<see cref="AppSettings.TaskHistoryCacheSeconds"/>)メモリキャッシュする(C-4+)。
    /// </summary>
    public static List<AgentTaskRecord> GetTaskHistory(string agentName, string? targetDirectory = null)
    {
        var settings = AppSettingsLoader.Load();
        var cacheKey = $"{agentName}|{targetDirectory}";

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) &&
                (DateTime.UtcNow - cached.CachedAtUtc).TotalSeconds < settings.TaskHistoryCacheSeconds)
            {
                return cached.Records;
            }
        }

        var records = new List<AgentTaskRecord>();

        // 1. Claude Code のログを走査
        try
        {
            records.AddRange(ScanClaudeLogs(agentName, targetDirectory));
        }
        catch
        {
            // ログ走査エラーは安全に無視
        }

        // 2. Gemini (Antigravity) のログを走査
        try
        {
            records.AddRange(ScanGeminiLogs(agentName, targetDirectory));
        }
        catch
        {
            // ログ走査エラーは安全に無視
        }

        var result = records
            .OrderByDescending(r => r.Timestamp)
            .Take(settings.MaxTaskHistoryRecords)
            .ToList();

        lock (_cacheLock)
        {
            _cache[cacheKey] = (DateTime.UtcNow, result);
        }

        return result;
    }

    /// <summary>
    /// Claude Codeの全プロジェクトディレクトリを走査し、直近更新された順に
    /// <see cref="AppSettings.TaskHistoryMaxScanFiles"/>件までのjsonlログから
    /// 指定エージェントのタスク履歴を抽出する。
    /// </summary>
    /// <param name="agentName">抽出対象のエージェント(サブエージェント)名。</param>
    /// <param name="targetDirectory">未使用(将来の対象ディレクトリ絞り込み用に予約)。</param>
    private static List<AgentTaskRecord> ScanClaudeLogs(string agentName, string? targetDirectory)
    {
        var settings = AppSettingsLoader.Load();
        var list = new List<AgentTaskRecord>();
        var claudeProjectsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        if (!Directory.Exists(claudeProjectsDir))
        {
            return list;
        }

        // 全プロジェクトディレクトリまたは対象ディレクトリを走査
        var scannedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectDirs = Directory.GetDirectories(claudeProjectsDir);
        foreach (var pDir in projectDirs)
        {
            var files = Directory.GetFiles(pDir, "*.jsonl");
            // 直近更新されたTaskHistoryMaxScanFiles件程度を対象とする
            var targetFiles = files
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(settings.TaskHistoryMaxScanFiles);

            foreach (var fi in targetFiles)
            {
                scannedFilePaths.Add(fi.FullName);
                try
                {
                    list.AddRange(ExtractClaudeRecordsFromFile(fi.FullName, agentName));
                }
                catch
                {
                    // 個別ファイルの破損はスキップ
                }
            }
        }

        EvictStaleClaudeFileIndexes(scannedFilePaths);

        return list;
    }

    /// <summary>
    /// 今回の走査対象から外れた(=更新順位が下がって古くなった、または削除された)
    /// jsonlファイルのインデックスを<see cref="_claudeFileIndexes"/>から破棄し、メモリリークを防ぐ。
    /// </summary>
    /// <param name="scannedFilePaths">今回の走査で対象となったファイルパスの集合。</param>
    private static void EvictStaleClaudeFileIndexes(HashSet<string> scannedFilePaths)
    {
        lock (_claudeIndexLock)
        {
            foreach (var staleKey in _claudeFileIndexes.Keys.Where(k => !scannedFilePaths.Contains(k)).ToList())
            {
                _claudeFileIndexes.Remove(staleKey);
            }
        }
    }

    /// <summary>
    /// Claudeログ1ファイルを増分読み込みし、生イベントインデックス(<see cref="ClaudeFileIndex"/>)に
    /// 反映したうえで、指定エージェント分のタスク履歴レコードを構築して返す。
    /// </summary>
    /// <param name="filePath">対象のjsonlログファイルの絶対パス。</param>
    /// <param name="agentName">抽出対象のエージェント(サブエージェント)名。</param>
    private static List<AgentTaskRecord> ExtractClaudeRecordsFromFile(string filePath, string agentName)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        var fileInfo = new FileInfo(filePath);

        ClaudeFileIndex index;
        lock (_claudeIndexLock)
        {
            if (!_claudeFileIndexes.TryGetValue(filePath, out index!))
            {
                index = new ClaudeFileIndex();
                _claudeFileIndexes[filePath] = index;
            }
        }

        var newLines = index.Reader.ReadNewLines(filePath);
        if (index.Reader.WasTruncated)
        {
            index.Invocations.Clear();
            index.ToolResults.Clear();
            index.AssistantTexts.Clear();
            index.UserOriginalRequestRawLine = null;
        }

        foreach (var line in newLines)
        {
            // ユーザーの元の依頼文を探索(最初に見つかった1件のみ保持)
            if (index.UserOriginalRequestRawLine == null && (line.Contains("\"lastPrompt\"") || line.Contains("\"role\":\"user\"")))
            {
                index.UserOriginalRequestRawLine = line;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var lineTimestamp = ExtractLineTimestamp(root, fileInfo.LastWriteTime);

                // 1. Agent ツール呼び出しの検出(agentNameによらず全件記録)
                if (root.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeProp) &&
                            typeProp.GetString() == "tool_use" &&
                            item.TryGetProperty("name", out var nameProp) &&
                            nameProp.GetString() == "Agent" &&
                            item.TryGetProperty("input", out var inputProp))
                        {
                            var targetName = inputProp.TryGetProperty("subagent_type", out var subProp) ? subProp.GetString() : null;
                            var toolUseId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                            if (!string.IsNullOrEmpty(toolUseId) && !string.IsNullOrEmpty(targetName))
                            {
                                var prompt = inputProp.TryGetProperty("prompt", out var promptProp) ? promptProp.GetString() : "指示内容未記録";
                                index.Invocations.Add(new ClaudeAgentInvocation(toolUseId, targetName, prompt ?? "指示内容未記録", lineTimestamp));
                            }
                        }
                    }
                }

                // 2. toolUseResult によるサブエージェント回答本文の検出(agentNameによらず全件記録)
                if (root.TryGetProperty("toolUseResult", out var turProp))
                {
                    var turPrompt = turProp.TryGetProperty("prompt", out var tp) ? tp.GetString() : null;
                    var turAgentType = turProp.TryGetProperty("agentType", out var at) ? at.GetString() : null;

                    // 同じ行のmessage.content内のtool_resultからtool_use_idを取得し、
                    // 文字列一致ではなく正式なIDでpendingRecordsと突合する (C-4)。
                    string? resultToolUseId = null;
                    if (root.TryGetProperty("message", out var turMessage) &&
                        turMessage.TryGetProperty("content", out var turMessageContent) &&
                        turMessageContent.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in turMessageContent.EnumerateArray())
                        {
                            if (c.TryGetProperty("type", out var ctProp) && ctProp.GetString() == "tool_result" &&
                                c.TryGetProperty("tool_use_id", out var idProp))
                            {
                                resultToolUseId = idProp.GetString();
                                break;
                            }
                        }
                    }

                    string? resultText = null;
                    if (turProp.TryGetProperty("content", out var turContent) && turContent.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in turContent.EnumerateArray())
                        {
                            if (c.TryGetProperty("text", out var tProp))
                            {
                                resultText = tProp.GetString();
                                break;
                            }
                        }
                    }

                    index.ToolResults.Add(new ClaudeToolResultEvent(resultToolUseId, turPrompt, turAgentType, resultText, lineTimestamp));
                }

                // 3. アシスタント最終回答テキストの追跡(フォールバック用)
                if (root.TryGetProperty("type", out var rowType) && rowType.GetString() == "assistant" &&
                    root.TryGetProperty("message", out var aMsg) &&
                    aMsg.TryGetProperty("content", out var aContent) &&
                    aContent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in aContent.EnumerateArray())
                    {
                        if (c.TryGetProperty("type", out var ct) && ct.GetString() == "text" &&
                            c.TryGetProperty("text", out var txtProp))
                        {
                            var text = txtProp.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                index.AssistantTexts.Add(text);
                            }
                        }
                    }
                }
            }
            catch
            {
                // 行パース失敗はスキップ
            }
        }

        return BuildClaudeRecordsForAgent(index, sessionId, agentName);
    }

    /// <summary>
    /// 増分走査済みの生イベントインデックスから、指定エージェント分の履歴レコードを構築する(G-6/C-5)。
    /// ファイルI/O・JSONパースは<see cref="ExtractClaudeRecordsFromFile"/>側で増分済みのため、
    /// ここではメモリ上のイベント一覧をエージェント名でフィルタするだけの軽量な処理となる。
    /// </summary>
    private static List<AgentTaskRecord> BuildClaudeRecordsForAgent(ClaudeFileIndex index, string sessionId, string agentName)
    {
        var list = new List<AgentTaskRecord>();
        var userOriginalRequest = index.UserOriginalRequestRawLine != null
            ? ExtractClaudeUserRequest(index.UserOriginalRequestRawLine, agentName)
            : null;

        var pendingRecords = new Dictionary<string, (string prompt, DateTime timestamp)>();
        foreach (var inv in index.Invocations)
        {
            if (string.Equals(inv.TargetAgentName, agentName, StringComparison.OrdinalIgnoreCase))
            {
                pendingRecords[inv.ToolUseId] = (inv.Prompt, inv.Timestamp);
            }
        }

        var lastAssistantText = index.AssistantTexts.Count > 0 ? index.AssistantTexts[^1] : null;

        foreach (var tur in index.ToolResults)
        {
            (string prompt, DateTime timestamp) matchedPending = default;
            var matchedById = tur.ResultToolUseId != null && pendingRecords.TryGetValue(tur.ResultToolUseId, out matchedPending);

            if (matchedById ||
                string.Equals(tur.TurAgentType, agentName, StringComparison.OrdinalIgnoreCase) ||
                (tur.TurPrompt != null && pendingRecords.Values.Any(p => p.prompt == tur.TurPrompt)))
            {
                if (!string.IsNullOrEmpty(tur.ResultText))
                {
                    var prompt = tur.TurPrompt ?? (matchedById ? matchedPending.prompt : null)
                        ?? (pendingRecords.Count > 0 ? pendingRecords.Values.Last().prompt : "指示内容未記録");
                    list.Add(new AgentTaskRecord(
                        SessionId: sessionId.Length > 8 ? sessionId[..8] : sessionId,
                        Engine: "Claude",
                        Timestamp: tur.Timestamp,
                        Instruction: prompt,
                        Status: "Completed",
                        ResultSummary: tur.ResultText,
                        UserRequest: userOriginalRequest));
                }

                if (tur.ResultToolUseId != null)
                {
                    // 突合できたエントリは後段のフォールバック処理で二重に扱われないよう除去する
                    pendingRecords.Remove(tur.ResultToolUseId);
                }
            }
        }

        // toolUseResultが取れなかったがAgentツール呼び出しがあった場合のフォールバック
        if (list.Count == 0 && pendingRecords.Count > 0)
        {
            foreach (var (_, (prompt, timestamp)) in pendingRecords)
            {
                list.Add(new AgentTaskRecord(
                    SessionId: sessionId.Length > 8 ? sessionId[..8] : sessionId,
                    Engine: "Claude",
                    Timestamp: timestamp,
                    Instruction: prompt,
                    Status: "Completed",
                    ResultSummary: lastAssistantText ?? "Claude Code セッションで完了",
                    UserRequest: userOriginalRequest));
            }
        }

        return list;
    }

    /// <summary>
    /// Gemini (Antigravity) の全会話ディレクトリを走査し、各transcript.jsonlから
    /// 指定エージェントのタスク履歴を抽出する。
    /// </summary>
    /// <param name="agentName">抽出対象のエージェント(サブエージェント)名。</param>
    /// <param name="targetDirectory">未使用(将来の対象ディレクトリ絞り込み用に予約)。</param>
    private static List<AgentTaskRecord> ScanGeminiLogs(string agentName, string? targetDirectory)
    {
        var list = new List<AgentTaskRecord>();
        var brainDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "brain");

        if (!Directory.Exists(brainDir))
        {
            return list;
        }

        var scannedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var convDirs = Directory.GetDirectories(brainDir);
        foreach (var cDir in convDirs)
        {
            var transcriptPath = Path.Combine(cDir, ".system_generated", "logs", "transcript.jsonl");
            if (!File.Exists(transcriptPath))
            {
                continue;
            }

            scannedFilePaths.Add(transcriptPath);
            try
            {
                list.AddRange(ExtractGeminiRecordsFromFile(transcriptPath, agentName, Path.GetFileName(cDir)));
            }
            catch
            {
                // 個別ファイル失敗はスキップ
            }
        }

        EvictStaleGeminiFileIndexes(scannedFilePaths);

        return list;
    }

    /// <summary>
    /// 今回の走査対象から外れた(=会話ディレクトリが削除された等の)transcriptファイルの
    /// インデックスを<see cref="_geminiFileIndexes"/>から破棄し、メモリリークを防ぐ。
    /// </summary>
    /// <param name="scannedFilePaths">今回の走査で対象となったファイルパスの集合。</param>
    private static void EvictStaleGeminiFileIndexes(HashSet<string> scannedFilePaths)
    {
        lock (_geminiIndexLock)
        {
            foreach (var staleKey in _geminiFileIndexes.Keys.Where(k => !scannedFilePaths.Contains(k)).ToList())
            {
                _geminiFileIndexes.Remove(staleKey);
            }
        }
    }

    /// <summary>
    /// Geminiのtranscript.jsonl1ファイルを増分読み込みし、生イベントインデックス
    /// (<see cref="GeminiFileIndex"/>)に反映したうえで、指定エージェント分のタスク履歴レコードを構築して返す。
    /// </summary>
    /// <param name="filePath">対象のtranscript.jsonlファイルの絶対パス。</param>
    /// <param name="agentName">抽出対象のエージェント(サブエージェント)名。</param>
    /// <param name="convId">会話ID(セッションID、ディレクトリ名由来)。</param>
    private static List<AgentTaskRecord> ExtractGeminiRecordsFromFile(string filePath, string agentName, string convId)
    {
        var fi = new FileInfo(filePath);

        GeminiFileIndex index;
        lock (_geminiIndexLock)
        {
            if (!_geminiFileIndexes.TryGetValue(filePath, out index!))
            {
                index = new GeminiFileIndex();
                _geminiFileIndexes[filePath] = index;
            }
        }

        var newLines = index.Reader.ReadNewLines(filePath);
        if (index.Reader.WasTruncated)
        {
            index.Invocations.Clear();
            index.UserOriginalRequestRawLine = null;
            index.LastModelResponse = null;
        }

        foreach (var line in newLines)
        {
            // ユーザーの元の依頼文を探索(最初に見つかった1件のみ保持)
            if (index.UserOriginalRequestRawLine == null && line.Contains("\"USER_INPUT\""))
            {
                index.UserOriginalRequestRawLine = line;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var lineTimestamp = ExtractLineTimestamp(root, fi.LastWriteTime);

                // 最終レスポンステキストの追跡
                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "PLANNER_RESPONSE" &&
                    root.TryGetProperty("content", out var contentProp))
                {
                    var text = contentProp.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        index.LastModelResponse = text;
                    }
                }

                // invoke_subagent の検出(agentNameによらず全件記録)
                if (root.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in toolCalls.EnumerateArray())
                    {
                        if (tc.TryGetProperty("name", out var tcName) &&
                            tcName.GetString() == "invoke_subagent" &&
                            tc.TryGetProperty("args", out var argsProp) &&
                            argsProp.TryGetProperty("Subagents", out var subagents) &&
                            subagents.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var sub in subagents.EnumerateArray())
                            {
                                var typeName = sub.TryGetProperty("TypeName", out var tProp) ? tProp.GetString() : null;
                                var role = sub.TryGetProperty("Role", out var rProp) ? rProp.GetString() : null;

                                if (!string.IsNullOrEmpty(typeName))
                                {
                                    var prompt = sub.TryGetProperty("Prompt", out var pProp) ? pProp.GetString() : "指示内容未記録";
                                    index.Invocations.Add(new GeminiSubagentInvocation(typeName, role, prompt ?? "指示内容未記録", lineTimestamp));
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // パースエラーはスキップ
            }
        }

        var list = new List<AgentTaskRecord>();
        var userOriginalRequest = index.UserOriginalRequestRawLine != null
            ? ExtractGeminiUserRequest(index.UserOriginalRequestRawLine)
            : null;

        foreach (var inv in index.Invocations)
        {
            if (string.Equals(inv.TypeName, agentName, StringComparison.OrdinalIgnoreCase) ||
                (inv.Role?.Contains(agentName, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                list.Add(new AgentTaskRecord(
                    SessionId: convId.Length > 8 ? convId[..8] : convId,
                    Engine: "Gemini",
                    Timestamp: inv.Timestamp,
                    Instruction: inv.Prompt,
                    Status: "Completed",
                    ResultSummary: index.LastModelResponse ?? "Gemini (Antigravity) サブエージェントで完了",
                    UserRequest: userOriginalRequest));
            }
        }

        return list;
    }

    /// <summary>
    /// JSONL1行分の個別タイムスタンプ(root直下の"timestamp")を読み取る。
    /// 存在しない・パース失敗の場合はファイル更新日時にフォールバックする (C-3)。
    /// </summary>
    private static DateTime ExtractLineTimestamp(JsonElement root, DateTime fallback)
    {
        if (root.TryGetProperty("timestamp", out var tsProp) && tsProp.ValueKind == JsonValueKind.String)
        {
            var raw = tsProp.GetString();
            if (!string.IsNullOrEmpty(raw) &&
                DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    /// <summary>
    /// ユーザーの元の依頼文が含まれるJSONL1行から、依頼テキストを抽出する。
    /// "lastPrompt"フィールド、またはrole="user"のmessage.contentから読み取る。
    /// </summary>
    /// <param name="line">解析対象のJSONL1行分の文字列。</param>
    /// <param name="agentName">依頼文中の定型プレフィックス除去に使うエージェント名。</param>
    private static string? ExtractClaudeUserRequest(string line, string agentName)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("lastPrompt", out var lpProp))
            {
                var prompt = lpProp.GetString();
                if (!string.IsNullOrEmpty(prompt))
                {
                    return CleanUserPrompt(prompt, agentName);
                }
            }

            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("role", out var role) && role.GetString() == "user" &&
                msg.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        return CleanUserPrompt(text, agentName);
                    }
                }
                else if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in content.EnumerateArray())
                    {
                        if (c.TryGetProperty("type", out var ct) && ct.GetString() == "text" &&
                            c.TryGetProperty("text", out var tp))
                        {
                            var text = tp.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                return CleanUserPrompt(text, agentName);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// ユーザー依頼文から"Please use subagent XXX to:"等の定型プレフィックスを除去する。
    /// </summary>
    /// <param name="raw">除去前の依頼文(生テキスト)。</param>
    /// <param name="agentName">プレフィックス中に埋め込まれるエージェント名。</param>
    private static string CleanUserPrompt(string raw, string agentName)
    {
        var text = raw.Trim();
        string[] prefixes = [
            $"Please use subagent {agentName} to:",
            $"Please use skill {agentName} to:",
            "Please use subagent to:",
            "Please use skill to:"
        ];

        foreach (var p in prefixes)
        {
            var idx = text.IndexOf(p, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return text[(idx + p.Length)..].Trim();
            }
        }

        return text;
    }

    /// <summary>
    /// Geminiのtype="USER_INPUT"な行から、&lt;USER_REQUEST&gt;タグ内(あれば)のユーザー依頼文を抽出する。
    /// </summary>
    /// <param name="line">解析対象のJSONL1行分の文字列。</param>
    private static string? ExtractGeminiUserRequest(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "USER_INPUT" &&
                root.TryGetProperty("content", out var cProp))
            {
                var content = cProp.GetString();
                if (!string.IsNullOrEmpty(content))
                {
                    var start = content.IndexOf("<USER_REQUEST>", StringComparison.OrdinalIgnoreCase);
                    var end = content.IndexOf("</USER_REQUEST>", StringComparison.OrdinalIgnoreCase);
                    if (start >= 0 && end > start)
                    {
                        return content.Substring(start + 14, end - (start + 14)).Trim();
                    }
                    return content.Trim();
                }
            }
        }
        catch { }
        return null;
    }
}
