using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentDeskApp;

/// <summary>
/// スタジオ全体のメンバー1件と、その所属先の情報(BUG-18: 同名チェック用)。
/// </summary>
/// <param name="Agent">メンバー定義。</param>
/// <param name="ScopeLabel">所属先の表示ラベル(例: "全社", "Sloth Studio / AgentDeskApp")。</param>
/// <param name="ClaudeDir">所属先の.claude/agentsフォルダの絶対パス(異動先の判定に使う)。</param>
public sealed record StudioAgentEntry(AgentDefinition Agent, string ScopeLabel, string ClaudeDir);

/// <summary>重複していた項目の種別。</summary>
public enum NameConflictKind
{
    /// <summary>Id(識別子・ファイル名)が重複。</summary>
    Id,

    /// <summary>DisplayName(呼び名)が重複。</summary>
    DisplayName,
}

/// <summary>新しい名前と衝突した既存メンバー。</summary>
/// <param name="Existing">衝突した既存メンバー。</param>
/// <param name="Kind">衝突した項目。</param>
public sealed record NameConflict(StudioAgentEntry Existing, NameConflictKind Kind)
{
    /// <summary>ユーザーに見せる衝突理由の説明文を返す。</summary>
    public string ToMessage()
    {
        var existingName = Existing.Agent.EffectiveDisplayName;
        return Kind == NameConflictKind.Id
            ? $"識別子(Id)「{Existing.Agent.Name}」は「{Existing.ScopeLabel}」の「{existingName}」が既に使っています。"
            : $"呼び名「{AgentNameUniqueness.ExtractCallName(Existing.Agent.DisplayName)}」は「{Existing.ScopeLabel}」の「{existingName}」が既に使っています。";
    }
}

/// <summary>既存データ内で同じIdまたは呼び名を共有しているメンバーの集まり(救済表示用)。</summary>
/// <param name="Kind">重複している項目。</param>
/// <param name="Key">重複している値(表示用)。</param>
/// <param name="Members">重複しているメンバー(2件以上)。</param>
public sealed record DuplicateGroup(NameConflictKind Kind, string Key, IReadOnlyList<StudioAgentEntry> Members);

/// <summary>
/// スタジオ全体で「Id」と「DisplayName(呼び名)」を一意にするための判定ロジック(BUG-18)。
/// 状態管理が名前キーのため、同名メンバーがいると全チームの同名カードが同時に作業中・完了になってしまう。
/// 呼び名は「QA担当 (イジワル)」なら括弧内の「イジワル」、括弧が無ければ表示名全体を指す。
/// 比較は全角半角・大文字小文字を区別しない。
/// </summary>
public static class AgentNameUniqueness
{
    /// <summary>複製した表示名の末尾に足す文言。</summary>
    private const string CopyDisplayNameSuffix = "（コピー）";

    private static readonly Regex TrailingParen = new(@"\(([^()]+)\)\s*$", RegexOptions.Compiled);

    /// <summary>比較用に文字列を正規化する(全角半角統一・前後空白除去・小文字化)。</summary>
    /// <param name="value">正規化対象。</param>
    internal static string NormalizeKey(string? value) =>
        (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();

    /// <summary>
    /// DisplayNameから呼び名を取り出す。末尾の括弧内(半角・全角どちらも可)があればその中身、無ければ表示名全体。
    /// </summary>
    /// <param name="displayName">表示名(例: "QA担当 (イジワル)")。</param>
    /// <returns>呼び名(例: "イジワル")。表示名が空なら空文字。</returns>
    public static string ExtractCallName(string? displayName)
    {
        var normalized = (displayName ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
        var match = TrailingParen.Match(normalized);
        return match.Success ? match.Groups[1].Value.Trim() : normalized;
    }

    /// <summary>
    /// 指定した名前が既存メンバーと衝突するかを調べる。最初に見つかった衝突を返す。
    /// </summary>
    /// <param name="id">新しいId(識別子)。空ならId比較はしない。</param>
    /// <param name="displayName">新しいDisplayName。空なら呼び名比較はしない。</param>
    /// <param name="entries">比較対象(スタジオ全体、または異動先のメンバー)。</param>
    /// <param name="self">編集中・異動中の本人。比較対象から除外する。新規ならnull。</param>
    /// <param name="checkId">Id比較を行うか。Idを変更できない編集では既存データ救済のためfalseにする。</param>
    public static NameConflict? FindConflict(
        string? id, string? displayName, IEnumerable<StudioAgentEntry> entries, AgentDefinition? self = null, bool checkId = true)
    {
        var idKey = NormalizeKey(id);
        var callKey = NormalizeKey(ExtractCallName(displayName));

        foreach (var entry in entries)
        {
            if (IsSame(entry.Agent, self))
            {
                continue;
            }

            if (checkId && idKey.Length > 0 && NormalizeKey(entry.Agent.Name) == idKey)
            {
                return new NameConflict(entry, NameConflictKind.Id);
            }

            if (callKey.Length > 0 &&
                !string.IsNullOrWhiteSpace(entry.Agent.DisplayName) &&
                NormalizeKey(ExtractCallName(entry.Agent.DisplayName)) == callKey)
            {
                return new NameConflict(entry, NameConflictKind.DisplayName);
            }
        }

        return null;
    }

    /// <summary>
    /// 既存データ内でIdまたは呼び名が重複しているメンバーの組を洗い出す(救済表示用)。
    /// 同じ定義ファイルを指すエントリは1件に畳む。
    /// </summary>
    /// <param name="entries">スタジオ全体のメンバー。</param>
    public static IReadOnlyList<DuplicateGroup> FindDuplicates(IEnumerable<StudioAgentEntry> entries)
    {
        var unique = entries
            .GroupBy(e => e.Agent.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var result = new List<DuplicateGroup>();

        foreach (var group in unique.GroupBy(e => NormalizeKey(e.Agent.Name)).Where(g => g.Key.Length > 0 && g.Count() > 1))
        {
            result.Add(new DuplicateGroup(NameConflictKind.Id, group.First().Agent.Name, group.ToList()));
        }

        var withCallName = unique.Where(e => !string.IsNullOrWhiteSpace(e.Agent.DisplayName));
        foreach (var group in withCallName.GroupBy(e => NormalizeKey(ExtractCallName(e.Agent.DisplayName))).Where(g => g.Key.Length > 0 && g.Count() > 1))
        {
            result.Add(new DuplicateGroup(NameConflictKind.DisplayName, ExtractCallName(group.First().Agent.DisplayName), group.ToList()));
        }

        return result;
    }

    /// <summary>
    /// 同一スコープ(同じチーム/グループ内)でIdまたは呼び名の重複があるか調べる(BUG-18改修)。
    /// 別グループ・別チーム間の同名は重複とみなさない(Q-5)。
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> FindDuplicatesInSameScope(IEnumerable<StudioAgentEntry> entries)
    {
        var result = new List<DuplicateGroup>();
        foreach (var scopeGroup in entries.GroupBy(e => e.ClaudeDir, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(scopeGroup.Key))
            {
                continue;
            }
            result.AddRange(FindDuplicates(scopeGroup));
        }

        return result;
    }

    /// <summary>
    /// 複製(クローン)用に、既存と衝突しないDisplayNameを組み立てる。
    /// 「QA担当 (イジワル)」なら「QA担当 (イジワル)（コピー）」「QA担当 (イジワル)（コピー2）」…のように、元の表示名の末尾に「（コピー）」を足す。
    /// </summary>
    /// <param name="sourceDisplayName">複製元の表示名。</param>
    /// <param name="entries">比較対象(スタジオ全体)。</param>
    public static string SuggestCopyDisplayName(string sourceDisplayName, IReadOnlyCollection<StudioAgentEntry> entries)
    {
        var trimmed = sourceDisplayName.Trim();
        for (var n = 1; ; n++)
        {
            // 元の表示名はそのまま残し、末尾に「（コピー）」を足す(括弧内の呼び名に連結すると読みにくいため)
            var suffix = n == 1 ? CopyDisplayNameSuffix : $"（コピー{n}）";
            var candidate = $"{trimmed}{suffix}";
            if (FindConflict(null, candidate, entries) is null)
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 2つのメンバー定義が同じ実体かを判定する(定義ファイルまたはGeminiスキルのパスが一致)。
    /// </summary>
    /// <param name="a">比較対象A。</param>
    /// <param name="b">比較対象B。nullなら常にfalse。</param>
    private static bool IsSame(AgentDefinition a, AgentDefinition? b)
    {
        if (b is null)
        {
            return false;
        }

        return PathEquals(a.FilePath, b.FilePath) ||
               PathEquals(a.FilePath, b.GeminiSkillPath) ||
               PathEquals(a.GeminiSkillPath, b.FilePath);
    }

    /// <summary>2つのパスが大文字小文字を無視して同じ場所を指すか(どちらかが空ならfalse)。</summary>
    internal static bool PathEquals(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 異動先フォルダ(.claude/agents)に所属するメンバーだけに絞り込む。
    /// </summary>
    /// <param name="entries">スタジオ全体のメンバー。</param>
    /// <param name="targetClaudeDir">異動先の.claude/agentsフォルダ。</param>
    public static IEnumerable<StudioAgentEntry> InScope(IEnumerable<StudioAgentEntry> entries, string targetClaudeDir) =>
        entries.Where(e => PathEquals(e.ClaudeDir, targetClaudeDir));
}
