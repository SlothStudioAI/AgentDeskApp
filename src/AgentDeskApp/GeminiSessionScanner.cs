using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentDeskApp;

/// <summary>
/// Gemini (Antigravity) のセッション探索結果。
/// </summary>
/// <param name="ConversationId">会話ID(セッションID)。</param>
/// <param name="Cwd">作業ディレクトリ(検出できない場合はnull)。</param>
/// <param name="State">稼働状態(Running / Done / Idle)。</param>
/// <param name="LastWorkPath">直近で触ったファイルパス(あれば)。</param>
/// <param name="LastModified">最終更新日時。</param>
/// <param name="ActiveSubagents">呼び出されたサブエージェント(メンバー)名のリスト(あれば)。スキルファイルのパスと invoke_subagent の TypeName 由来。</param>
/// <param name="SubagentInvocations">invoke_subagent の呼び出し内容(TypeNameとRoleの組、生の値)のリスト(あれば)。
/// TypeName が "self" などの汎用の型の場合に Role からメンバーを特定するため、照合は<see cref="MemberActivityMonitor"/>側で行う。</param>
public sealed record GeminiSessionRecord(
    string ConversationId,
    string? Cwd,
    MemberActivityState State,
    string? LastWorkPath,
    DateTime LastModified,
    IReadOnlyList<string>? ActiveSubagents = null,
    IReadOnlyList<GeminiSubagentInvocation>? SubagentInvocations = null);

/// <summary>
/// Gemini (Antigravity) の invoke_subagent 1件分の呼び出し内容。
/// </summary>
/// <param name="TypeName">サブエージェントの型名("self"・"research"などの汎用の型、またはdefine_subagentで定義したメンバー名)。取得できなければnull。</param>
/// <param name="Role">役割名(例: "ビジュアル担当 (エガク)")。取得できなければnull。</param>
public sealed record GeminiSubagentInvocation(string? TypeName, string? Role);

/// <summary>
/// Antigravity (Gemini) のローカルセッションログ(~/.gemini/antigravity/brain/)をスキャンし、
/// 現在アクティブなGeminiリーダーセッションを検出するスキャナー。
/// </summary>
public static class GeminiSessionScanner
{
    /// <summary>
    /// Antigravity の会話ログ格納先(~/.gemini/antigravity/brain)。
    /// </summary>
    private static readonly string BrainDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "brain");

    /// <summary>
    /// ログ側で長いPromptが「&lt;truncated N bytes&gt;」に省略され、Subagents の JSON 文字列が壊れている場合の
    /// TypeName フォールバック抽出用パターン。エスケープされていない引用符で始まるキーだけを拾うため、
    /// Prompt本文中(エスケープ済み)の同名文字列には反応しない。
    /// </summary>
    private static readonly Regex TypeNamePattern = new(@"""TypeName""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    /// <summary><see cref="TypeNamePattern"/>と同じく、Role のフォールバック抽出用パターン。</summary>
    private static readonly Regex RolePattern = new(@"""Role""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    /// <summary>
    /// 会話ID(セッションID)ごとの解析済み蓄積状態(G-6/C-5)。<see cref="IncrementalLineReader"/>で
    /// 読み取った追記分だけをこの状態へ反映することで、毎回のファイル全文再読み込み・再パースを避ける。
    /// </summary>
    /// <remarks>
    /// <see cref="ScanActiveSessions"/>はMainWindowのDispatcherTimerからUIスレッド上で
    /// 単一スレッド呼び出しされる前提であり、この蓄積状態への読み書き(<see cref="ApplyLineToAccumulator"/>含む)は
    /// 呼び出し元でロック取得後の同一スレッド内でのみ行われるため、フィールド自体には個別の同期を設けていない。
    /// </remarks>
    private sealed class TranscriptAccumulator
    {
        public IncrementalLineReader Reader { get; } = new();
        public string? DetectedCwd;
        public string? LastWorkPath;
        public string? LastLine;
        public HashSet<string> DetectedSubagents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<GeminiSubagentInvocation> SubagentInvocations { get; } = [];
    }

    private static readonly Dictionary<string, TranscriptAccumulator> _accumulators = new();
    private static readonly object _accumulatorsLock = new();

    /// <summary>
    /// 直近にアクティブだったGeminiセッションを走査して返す。
    /// </summary>
    /// <param name="activeWindowMinutes">アクティブとみなす更新時間枠(分、既定値180分=3時間)。</param>
    public static List<GeminiSessionRecord> ScanActiveSessions(int activeWindowMinutes = 180) =>
        ScanActiveSessions(activeWindowMinutes, BrainDir);

    /// <summary>
    /// 指定した brain フォルダ配下の、直近にアクティブだったGeminiセッションを走査して返す。
    /// %USERPROFILE%を差し替えても既定の格納先は変わらないため、単体テストでは一時フォルダを直接渡す。
    /// </summary>
    /// <param name="activeWindowMinutes">アクティブとみなす更新時間枠(分)。</param>
    /// <param name="brainDir">走査する brain フォルダ(会話IDごとのサブフォルダを含む)。</param>
    public static List<GeminiSessionRecord> ScanActiveSessions(int activeWindowMinutes, string brainDir)
    {
        var results = new List<GeminiSessionRecord>();

        if (!Directory.Exists(brainDir))
        {
            return results;
        }

        var threshold = DateTime.UtcNow.AddMinutes(-activeWindowMinutes);
        var convDirs = Directory.GetDirectories(brainDir);
        var activeConvIds = new HashSet<string>();

        foreach (var cDir in convDirs)
        {
            var transcriptPath = Path.Combine(cDir, ".system_generated", "logs", "transcript.jsonl");
            if (!File.Exists(transcriptPath))
            {
                continue;
            }

            var fi = new FileInfo(transcriptPath);
            if (fi.LastWriteTimeUtc < threshold)
            {
                continue;
            }

            var convId = Path.GetFileName(cDir);
            activeConvIds.Add(convId);
            try
            {
                var record = AnalyzeTranscriptIncremental(transcriptPath, convId, fi.LastWriteTime);
                if (record != null)
                {
                    results.Add(record);
                }
            }
            catch
            {
                // 個別ログの例外はスキップ
            }
        }

        // アクティブ時間枠から外れたセッションの蓄積状態は破棄し、メモリを解放する。
        lock (_accumulatorsLock)
        {
            foreach (var staleKey in _accumulators.Keys.Where(k => !activeConvIds.Contains(k)).ToList())
            {
                _accumulators.Remove(staleKey);
            }
        }

        return results;
    }

    /// <summary>
    /// 会話1件分のtranscript.jsonlを増分読み込みし、蓄積状態を更新したうえで
    /// 現在の稼働状態(Running/Done)を含むセッションレコードを構築する。
    /// </summary>
    /// <param name="filePath">対象のtranscript.jsonlファイルの絶対パス。</param>
    /// <param name="convId">会話ID(セッションID、ディレクトリ名由来)。</param>
    /// <param name="lastModified">ファイルの最終更新日時。</param>
    private static GeminiSessionRecord? AnalyzeTranscriptIncremental(string filePath, string convId, DateTime lastModified)
    {
        TranscriptAccumulator acc;
        lock (_accumulatorsLock)
        {
            if (!_accumulators.TryGetValue(convId, out acc!))
            {
                acc = new TranscriptAccumulator();
                _accumulators[convId] = acc;
            }
        }

        var newLines = acc.Reader.ReadNewLines(filePath);
        if (acc.Reader.WasTruncated)
        {
            // ファイルが縮小(再作成等)された場合は蓄積状態を破棄して読み直す。
            acc.DetectedCwd = null;
            acc.LastWorkPath = null;
            acc.LastLine = null;
            acc.DetectedSubagents.Clear();
            acc.SubagentInvocations.Clear();
        }

        foreach (var line in newLines)
        {
            acc.LastLine = line;
            ApplyLineToAccumulator(line, acc);
        }

        // 稼働状態判定: 末尾行で正確に判定
        // - ユーザーが入力した直後(AI回答待ち)、またはツール実行中、またはRUNNINGステータス = 作業中(Running / Active)
        // - MODELが回答を完了してユーザーの入力待ち = 完了・待機(Done)
        var state = MemberActivityState.Done;
        if (!string.IsNullOrEmpty(acc.LastLine))
        {
            try
            {
                using var lastDoc = System.Text.Json.JsonDocument.Parse(acc.LastLine);
                var lastRoot = lastDoc.RootElement;
                var type = lastRoot.TryGetProperty("type", out var tp) ? tp.GetString() : null;
                var status = lastRoot.TryGetProperty("status", out var sp) ? sp.GetString() : null;
                var hasToolCalls = lastRoot.TryGetProperty("tool_calls", out var tc) &&
                                   tc.ValueKind == System.Text.Json.JsonValueKind.Array &&
                                   tc.GetArrayLength() > 0;

                if (type == "USER_INPUT" || status == "RUNNING" || hasToolCalls)
                {
                    state = MemberActivityState.Running;
                }
                else
                {
                    state = MemberActivityState.Done;
                }
            }
            catch
            {
                state = MemberActivityState.Done;
            }
        }

        var detectedCwd = acc.DetectedCwd;
        var lastWorkPath = acc.LastWorkPath;

        // Cwdが未検出でもlastWorkPathがあれば親ディレクトリから補完
        if (string.IsNullOrEmpty(detectedCwd) && !string.IsNullOrEmpty(lastWorkPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(lastWorkPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    detectedCwd = dir;
                }
            }
            catch
            {
                // 無効なパスは無視
            }
        }

        return new GeminiSessionRecord(
            ConversationId: convId,
            Cwd: detectedCwd,
            State: state,
            LastWorkPath: lastWorkPath,
            LastModified: lastModified,
            ActiveSubagents: acc.DetectedSubagents.Count > 0 ? acc.DetectedSubagents.ToList() : null,
            SubagentInvocations: acc.SubagentInvocations.Count > 0 ? acc.SubagentInvocations.ToList() : null);
    }

    /// <summary>会話ログ1行分を解析し、蓄積状態(Cwd/最終作業パス/検出サブエージェント)に反映する。</summary>
    private static void ApplyLineToAccumulator(string line, TranscriptAccumulator acc)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            var root = doc.RootElement;

            // tool_calls から Cwd, AbsolutePath / TargetFile, および invoke_subagent を安全に抽出
            if (root.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var tc in toolCalls.EnumerateArray())
                {
                    var toolName = tc.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

                    if (tc.TryGetProperty("args", out var args) && args.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (args.TryGetProperty("Cwd", out var cwdProp))
                        {
                            var cwdVal = CleanPath(cwdProp.GetString());
                            if (!string.IsNullOrEmpty(cwdVal))
                            {
                                acc.DetectedCwd = cwdVal;
                            }
                        }

                        if (args.TryGetProperty("AbsolutePath", out var absProp))
                        {
                            var absVal = CleanPath(absProp.GetString());
                            if (!string.IsNullOrEmpty(absVal))
                            {
                                acc.LastWorkPath = absVal;
                                CheckSkillPath(absVal, acc.DetectedSubagents);
                            }
                        }
                        else if (args.TryGetProperty("TargetFile", out var targetProp))
                        {
                            var targetVal = CleanPath(targetProp.GetString());
                            if (!string.IsNullOrEmpty(targetVal))
                            {
                                acc.LastWorkPath = targetVal;
                                CheckSkillPath(targetVal, acc.DetectedSubagents);
                            }
                        }

                        // invoke_subagent の呼び出し検出(TypeName と Role の組を生のまま記録する)
                        if (toolName == "invoke_subagent" && args.TryGetProperty("Subagents", out var subagentsProp))
                        {
                            foreach (var invocation in ParseSubagents(subagentsProp))
                            {
                                if (!string.IsNullOrWhiteSpace(invocation.TypeName))
                                {
                                    acc.DetectedSubagents.Add(invocation.TypeName);
                                }

                                if (!acc.SubagentInvocations.Contains(invocation))
                                {
                                    acc.SubagentInvocations.Add(invocation);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // JSONパースエラー(破損行など)は安全にスキップ
        }
    }

    /// <summary>
    /// transcript.jsonl の1行から invoke_subagent の呼び出し内容(TypeName と Role の組)をすべて取り出す。
    /// 単体テストで実ログ形式の行を直接検証できるよう公開している。
    /// </summary>
    /// <param name="line">transcript.jsonl の1行(JSON)。</param>
    /// <returns>呼び出し内容のリスト(invoke_subagent でない行・解析できない行は空)。</returns>
    public static IReadOnlyList<GeminiSubagentInvocation> ExtractSubagentInvocations(string line)
    {
        var acc = new TranscriptAccumulator();
        ApplyLineToAccumulator(line, acc);
        return acc.SubagentInvocations;
    }

    /// <summary>
    /// invoke_subagent の args.Subagents を解析する。実際のAntigravityのログでは配列が「JSON文字列」として
    /// 格納されている(例: "[{\"TypeName\":\"self\",\"Role\":\"...\"}]")ため、文字列なら中身を再パースする。
    /// 長いPromptが省略されてJSONとして壊れている場合は、TypeName/Role のキーを正規表現で拾うフォールバックを行う。
    /// </summary>
    /// <param name="subagentsProp">args.Subagents の値(配列、またはJSON配列を表す文字列)。</param>
    private static List<GeminiSubagentInvocation> ParseSubagents(JsonElement subagentsProp)
    {
        if (subagentsProp.ValueKind == JsonValueKind.Array)
        {
            return ReadInvocations(subagentsProp);
        }

        if (subagentsProp.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var raw = subagentsProp.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            using var innerDoc = JsonDocument.Parse(raw);
            if (innerDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return ReadInvocations(innerDoc.RootElement);
            }
        }
        catch (JsonException)
        {
            // 省略記号などで壊れたJSON → 下の正規表現フォールバックへ
        }

        return ExtractInvocationsByPattern(raw);
    }

    /// <summary>JSON配列の各要素から TypeName と Role を読み取る。</summary>
    /// <param name="array">Subagents の配列要素。</param>
    private static List<GeminiSubagentInvocation> ReadInvocations(JsonElement array)
    {
        var result = new List<GeminiSubagentInvocation>();
        foreach (var sa in array.EnumerateArray())
        {
            if (sa.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var typeName = ReadTrimmedString(sa, "TypeName");
            var role = ReadTrimmedString(sa, "Role");
            if (typeName is not null || role is not null)
            {
                result.Add(new GeminiSubagentInvocation(typeName, role));
            }
        }

        return result;
    }

    /// <summary>オブジェクトから文字列プロパティを読み、前後の空白を除いて返す(無い・空ならnull)。</summary>
    /// <param name="obj">対象のJSONオブジェクト。</param>
    /// <param name="propertyName">プロパティ名。</param>
    private static string? ReadTrimmedString(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var value = prop.GetString()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    /// <summary>
    /// 壊れた Subagents 文字列から TypeName と Role を正規表現で拾う。
    /// 件数が揃っていれば出現順に組にし、揃わなければ TypeName と Role をそれぞれ単独の呼び出しとして扱う。
    /// </summary>
    /// <param name="raw">Subagents の生の文字列。</param>
    private static List<GeminiSubagentInvocation> ExtractInvocationsByPattern(string raw)
    {
        var typeNames = TypeNamePattern.Matches(raw).Select(m => UnescapeJsonString(m.Groups[1].Value)).ToList();
        var roles = RolePattern.Matches(raw).Select(m => UnescapeJsonString(m.Groups[1].Value)).ToList();

        var result = new List<GeminiSubagentInvocation>();
        if (typeNames.Count == roles.Count)
        {
            for (var i = 0; i < typeNames.Count; i++)
            {
                result.Add(new GeminiSubagentInvocation(typeNames[i], roles[i]));
            }
        }
        else
        {
            result.AddRange(typeNames.Select(t => new GeminiSubagentInvocation(t, null)));
            result.AddRange(roles.Select(r => new GeminiSubagentInvocation(null, r)));
        }

        return result.Where(r => r.TypeName is not null || r.Role is not null).ToList();
    }

    /// <summary>JSON文字列リテラルの中身(エスケープ込み)を通常の文字列へ戻す。失敗時はそのまま返す。空ならnull。</summary>
    /// <param name="escaped">引用符を除いたJSON文字列リテラルの中身。</param>
    private static string? UnescapeJsonString(string escaped)
    {
        string value;
        try
        {
            value = JsonSerializer.Deserialize<string>("\"" + escaped + "\"") ?? escaped;
        }
        catch (JsonException)
        {
            value = escaped;
        }

        value = value.Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// ファイルパスが".agents/skills/&lt;スキル名&gt;/..."形式であればスキル名をサブエージェント名として抽出し、
    /// 検出済みサブエージェント集合に追加する。
    /// </summary>
    /// <param name="path">検査対象のファイルパス。</param>
    /// <param name="subagents">検出結果を追加する蓄積先の集合。</param>
    private static void CheckSkillPath(string path, HashSet<string> subagents)
    {
        // 例: .agents/skills/code-reviewer/SKILL.md から "code-reviewer" を抽出
        if (path.Contains(".agents", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("skills", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("skills", StringComparison.OrdinalIgnoreCase))
                {
                    var skillName = parts[i + 1];
                    if (!skillName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        subagents.Add(skillName);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// JSON中のパス文字列から前後の空白・引用符・バックスラッシュや二重エスケープを取り除き、
    /// 整形されたパス文字列を返す(空・null時はnull)。
    /// </summary>
    /// <param name="raw">整形前のパス文字列(JSON値から取得した生の文字列)。</param>
    private static string? CleanPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = raw.Trim(' ', '"', '\\');
        return cleaned.Replace("\\\\", "\\");
    }
}
