using System.Text;

namespace AgentDeskApp;

/// <summary>
/// テンプレートと同じId(識別子)のメンバーが、追加先以外のスコープ(他チーム・ワークスペース直下・共通)に
/// 既にいるかを調べる、UIから分離した純粋なロジック。
/// 作業状態はId単位で管理されるため、別スコープの同Idメンバーとは作業状態が共有される。その案内表示に使う。
/// </summary>
public static class OtherScopeMemberFinder
{
    /// <summary>行のバッジ文言の接頭辞。</summary>
    public const string BadgePrefix = "ℹ️他のチームにも登録済み: ";

    /// <summary>行のツールチップ・画面下部の注記に使う、作業状態が共有される旨の文言。</summary>
    public const string SharedStateNote = "同じメンバーは他のチームにもいます。作業状態は共有されます";

    /// <summary>バッジ内で所属ラベルを区切る文字列。</summary>
    private const string LabelSeparator = "、";

    /// <summary>
    /// 指定Idのメンバーが所属している、追加先以外のスコープの所属ラベルを重複なく返す。
    /// Idの比較は大文字小文字・全角半角を区別しない。追加先スコープ自身のメンバーは除外する。
    /// </summary>
    /// <param name="templateId">テンプレートのId。</param>
    /// <param name="targetClaudeDir">追加先スコープの.claude/agentsフォルダ。</param>
    /// <param name="allEntries">スタジオ全体のメンバー。</param>
    /// <returns>他スコープの所属ラベル(出現順)。該当なしなら空。</returns>
    public static IReadOnlyList<string> FindOtherScopeLabels(
        string templateId, string targetClaudeDir, IEnumerable<StudioAgentEntry> allEntries)
    {
        var idKey = AgentNameUniqueness.NormalizeKey(templateId);
        if (idKey.Length == 0)
        {
            return [];
        }

        var labels = new List<string>();
        foreach (var entry in allEntries)
        {
            if (AgentNameUniqueness.NormalizeKey(entry.Agent.Name) != idKey ||
                AgentNameUniqueness.PathEquals(entry.ClaudeDir, targetClaudeDir))
            {
                continue;
            }

            if (!labels.Contains(entry.ScopeLabel, StringComparer.Ordinal))
            {
                labels.Add(entry.ScopeLabel);
            }
        }

        return labels;
    }

    /// <summary>
    /// 行に出すバッジ文言を組み立てる。
    /// </summary>
    /// <param name="labels">他スコープの所属ラベル。</param>
    /// <returns>バッジ文言。ラベルが空なら空文字。</returns>
    public static string BuildBadgeText(IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
        {
            return string.Empty;
        }

        return new StringBuilder(BadgePrefix).Append(string.Join(LabelSeparator, labels)).ToString();
    }
}
