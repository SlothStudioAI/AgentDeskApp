using System.Text;

namespace AgentDeskApp;

/// <summary>
/// Gemini(Antigravity)の invoke_subagent で指定された Role(役割名)から、担当メンバーを特定する純粋関数群。
/// Antigravity は TypeName に "self" などの汎用の型を指定し、具体的な役割を Role に書くため、
/// Role とメンバーの名前を「完全一致」でのみ照合する(説明文に名前が含まれるだけの部分一致は拾わない)。
/// 前後の空白・全角/半角の違い(かっこ含む)・英字の大文字小文字・かっこ前後の空白の違いは吸収する。
/// </summary>
public static class SubagentRoleMatcher
{
    /// <summary>
    /// Role に完全一致するメンバーを1人だけ特定して、そのIdを返す。
    /// 照合する名前は、メンバー側が「Id」「表示名全体」「表示名のかっこ内の呼び名」、
    /// Role 側が「Role全体」「Roleのかっこ内の呼び名(『ビジュアル担当 (エガク)』形式のとき)」
    /// 「Roleのかっこ外の部分(『サガス（リサーチ担当）』形式のとき)」。
    /// </summary>
    /// <param name="role">invoke_subagent の Role の値(生の文字列)。null/空なら一致なし。</param>
    /// <param name="candidates">照合対象のメンバー一覧。</param>
    /// <returns>一致したメンバーのId。一致なし、または複数メンバーに一致して特定できない場合はnull。</returns>
    public static string? Match(string? role, IEnumerable<PromptNominationCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var roleKeys = new HashSet<string>(StringComparer.Ordinal) { Normalize(role) };
        roleKeys.UnionWith(ExtractParenthesized(Normalize(role)));
        roleKeys.Add(ExtractOutsideParentheses(Normalize(role)));
        roleKeys.Remove(string.Empty);

        string? matchedId = null;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id))
            {
                continue;
            }

            if (!GetMemberNames(candidate).Overlaps(roleKeys))
            {
                continue;
            }

            if (matchedId is not null && !string.Equals(matchedId, candidate.Id, StringComparison.Ordinal))
            {
                // 複数メンバーに一致した場合は誤点灯を避けるため特定しない
                return null;
            }

            matchedId = candidate.Id;
        }

        return matchedId;
    }

    /// <summary>
    /// TypeName が既知メンバーのIdと一致する(大文字小文字は無視)場合に、そのメンバーのIdを返す。
    /// define_subagent で定義したメンバー名そのもので呼ばれたケースの判定に使う。
    /// </summary>
    /// <param name="typeName">invoke_subagent の TypeName の値。</param>
    /// <param name="candidates">照合対象のメンバー一覧。</param>
    /// <returns>一致したメンバーのId(表記は登録側に揃える)。一致しなければnull。</returns>
    public static string? MatchTypeName(string? typeName, IEnumerable<PromptNominationCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var trimmed = typeName.Trim();
        return candidates.FirstOrDefault(c => string.Equals(c.Id, trimmed, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>メンバー1人分の照合用の名前(Id・表示名全体・表示名のかっこ内の呼び名。正規化済み)を集める。</summary>
    /// <param name="candidate">メンバー1人分の名前情報。</param>
    private static HashSet<string> GetMemberNames(PromptNominationCandidate candidate)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { Normalize(candidate.Id) };
        if (!string.IsNullOrWhiteSpace(candidate.DisplayName))
        {
            var display = Normalize(candidate.DisplayName);
            names.Add(display);
            names.UnionWith(ExtractParenthesized(display));
        }

        names.Remove(string.Empty);
        return names;
    }

    /// <summary>正規化済み文字列から、半角かっこ「( )」で囲まれた部分をすべて取り出す。</summary>
    /// <param name="normalized">正規化済みの文字列(全角かっこは半角に変換済みであること)。</param>
    private static IEnumerable<string> ExtractParenthesized(string normalized)
    {
        var open = normalized.IndexOf('(');
        while (open >= 0)
        {
            var close = normalized.IndexOf(')', open + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return normalized[(open + 1)..close].Trim();
            open = normalized.IndexOf('(', close + 1);
        }
    }

    /// <summary>
    /// 正規化済み文字列から、半角かっこ「( )」で囲まれた部分をすべて取り除いた残り(前後の空白は除去)を返す。
    /// 「サガス(リサーチ担当)」のように呼び名がかっこの外にある形式から「サガス」を取り出すために使う。
    /// 閉じかっこが無い「(」以降はそのまま残す。
    /// </summary>
    /// <param name="normalized">正規化済みの文字列(全角かっこは半角に変換済みであること)。</param>
    private static string ExtractOutsideParentheses(string normalized)
    {
        var builder = new StringBuilder(normalized.Length);
        var index = 0;
        while (index < normalized.Length)
        {
            var open = normalized.IndexOf('(', index);
            var close = open >= 0 ? normalized.IndexOf(')', open + 1) : -1;
            if (open < 0 || close < 0)
            {
                builder.Append(normalized, index, normalized.Length - index);
                break;
            }

            builder.Append(normalized, index, open - index);
            index = close + 1;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// 照合用に正規化する。NFKC正規化(全角英数・全角かっこ・全角空白→半角)、小文字化、前後の空白除去に加え、
    /// 「ビジュアル担当(エガク)」と「ビジュアル担当 (エガク)」を同一視するため、かっこの前後の空白を取り除く。
    /// </summary>
    /// <param name="value">正規化前の文字列。</param>
    private static string Normalize(string value)
    {
        var text = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                // 直前が「(」、または次の非空白文字が「(」「)」なら、かっこ前後の空白として捨てる
                var next = i + 1;
                while (next < text.Length && char.IsWhiteSpace(text[next]))
                {
                    next++;
                }

                var prevIsOpen = builder.Length > 0 && builder[^1] == '(';
                var nextIsParen = next < text.Length && text[next] is '(' or ')';
                if (prevIsOpen || nextIsParen)
                {
                    continue;
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
