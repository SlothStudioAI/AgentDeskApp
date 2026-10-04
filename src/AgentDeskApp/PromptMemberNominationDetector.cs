using System.Text;

namespace AgentDeskApp;

/// <summary>
/// プロンプト中の指名検出に使う、メンバー1人分の名前情報(BUG-8)。
/// </summary>
/// <param name="Id">メンバーの識別名(frontmatterのname。状態の管理キーにも使う)。</param>
/// <param name="DisplayName">画面表示名(未設定ならnull)。</param>
/// <param name="AlternateDisplayNames">
/// 同じIdで別のスコープに定義された、異なる表示名(例: チームごとに表示名が違う/未設定のものと設定済みのものが混在)。
/// 後から読んだ定義の表示名が空でも、別スコープの表示名(呼び名)で照合できるようにするためのもの。無ければnull。
/// </param>
public sealed record PromptNominationCandidate(string Id, string? DisplayName = null, IReadOnlyList<string>? AlternateDisplayNames = null);

/// <summary>
/// リーダーがAgent/Taskツールでサブエージェントを起動する際の命令文から「対象メンバー」を判定する純粋関数群(BUG-8)。
/// 「〇〇に頼んで」「@〇〇」「〇〇さん、」のように名前が依頼の宛先として現れる指示形と、
/// 「あなたは〜『〇〇』です」「〇〇として」「担当: 〇〇」「【〇〇】」のような役割宣言形のみを拾い、
/// 「〇〇の設計を参考に」のような単なる言及や、別の単語の一部としての部分一致は拾わない(保守的な判定)。
/// 状態を持たず、WPFにも依存しないため単体テストしやすい。
/// </summary>
public static class PromptMemberNominationDetector
{
    /// <summary>名前が短すぎると誤検出が増えるため、この文字数未満の名前は対象外にする。</summary>
    private const int MinimumNameLength = 2;

    /// <summary>名前の直後に付くと宛先を示す助詞・記号(敬称なし)。</summary>
    private static readonly string[] AddresseeParticles = ["に", "へ", "、", ",", "，", ":", "：", "!", "！"];

    /// <summary>「、」系の区切り記号(直後/直前が別のメンバー名なら列挙とみなして不採用にする)。</summary>
    private static readonly string[] ListSeparators = ["、", ",", "，"];

    /// <summary>主題・手段を示す助詞。同じ文の中に依頼表現がある場合のみ指名とみなす(「で」単独は拾わない)。</summary>
    private static readonly string[] TopicParticles = ["は", "が", "で"];

    /// <summary>名前の直後に付く敬称(付いていれば指名とみなす。ただし後続が言及を示す語なら除外)。</summary>
    private static readonly string[] Honorifics = ["さん", "くん", "ちゃん", "君", "様", "さま"];

    /// <summary>敬称の直後に続くと「呼びかけ」ではなく「言及」になる語。</summary>
    private static readonly string[] MentionAfterHonorific = ["の", "から", "より"];

    /// <summary>助詞「に」の直後に続くと「宛先」ではなく「言及・比較」になる語(例: 〇〇について/〇〇に近い)。</summary>
    private static readonly string[] MentionAfterNi = ["ついて", "関し", "関す", "よる", "よって", "近", "似", "倣", "準", "比べ", "対する"];

    /// <summary>
    /// 「は/が/で」の後の文に含まれていれば依頼とみなす動詞の依頼表現(NFKC正規化・小文字化後の文字列と照合する)。
    /// 名詞の「実装」「担当」だけでは依頼とみなさない。
    /// </summary>
    private static readonly string[] RequestExpressions =
    [
        "して", "やって", "作って", "書いて", "直して", "調べて", "確認して", "見て", "頼", "お願い",
        "ください", "下さい", "よろしく", "しろ", "やれ", "せよ",
    ];

    /// <summary>名前を囲むと役割宣言とみなす開き括弧と、それに対応する閉じ括弧(「」『』【】)。</summary>
    private const string OpenBrackets = "「『【";

    /// <summary>OpenBracketsに対応する閉じ括弧(同じ添字が対)。</summary>
    private const string CloseBrackets = "」』】";

    /// <summary>名前の直後に続くと役割宣言とみなす語(「あなたは〜です」の文脈が必要なものは別扱い)。</summary>
    private static readonly string[] RoleSuffixes = ["として", "役"];

    /// <summary>名前の直後に続く断定表現(直前の文に「あなたは」がある場合のみ役割宣言とみなす)。</summary>
    private static readonly string[] CopulaSuffixes = ["です", "だ", "である", "でした"];

    /// <summary>「あなたは」に相当する二人称の主題表現(NFKC正規化・小文字化後)。</summary>
    private static readonly string[] SecondPersonMarkers = ["あなたは", "あなたが", "you are", "君は", "きみは"];

    /// <summary>名前の直前に付くと担当の指定とみなすラベル(直後に「:」が続く形)。</summary>
    private static readonly string[] AssignmentLabels = ["担当者", "担当"];

    /// <summary>文の区切りとみなす文字(「は/が」の依頼表現探索の範囲を決める)。</summary>
    private static readonly char[] SentenceSeparators = ['。', '.', '!', '?', '\n', '\r'];

    /// <summary>
    /// プロンプト文面から、指示形で名前が現れているメンバーのId集合を返す。
    /// 全角/半角・英字の大文字小文字の違いは無視する(NFKC正規化+大文字小文字無視)。
    /// </summary>
    /// <param name="prompt">Agent/Taskツールの命令文(prompt/description)。null/空なら空集合。</param>
    /// <param name="candidates">照合対象のメンバー一覧(Id・DisplayName全体・DisplayNameの括弧内の呼び名で照合する)。</param>
    /// <returns>指名されたメンバーのId集合。</returns>
    public static IReadOnlySet<string> Detect(string? prompt, IEnumerable<PromptNominationCandidate> candidates)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return result;
        }

        var text = Normalize(prompt);

        // 各メンバーの照合用の名前(Id・表示名全体・表示名の括弧内の呼び名)を先に集める。
        var namesByMember = new List<(string Id, List<string> Names)>();
        var allKnownNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id))
            {
                continue;
            }

            var names = GetMatchNames(candidate);
            namesByMember.Add((candidate.Id, names));
            allKnownNames.UnionWith(names);
        }

        foreach (var (id, names) in namesByMember)
        {
            if (names.Any(name => ContainsNomination(text, name, allKnownNames)))
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <summary>
    /// メンバー1人分の照合用の名前一覧(正規化済み)を作る。IdとDisplayName全体に加え、
    /// 「QA担当 (イジワル)」のような表示名の括弧(半角・全角)内の呼び名も候補にする。
    /// 短すぎる名前と、英字2文字のみの名前(qa/ui/pm等)は誤検出が多いため対象外。
    /// </summary>
    /// <param name="candidate">メンバー1人分の名前情報。</param>
    private static List<string> GetMatchNames(PromptNominationCandidate candidate)
    {
        var raw = new List<string> { candidate.Id };
        var displayNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(candidate.DisplayName))
        {
            displayNames.Add(candidate.DisplayName);
        }

        if (candidate.AlternateDisplayNames is not null)
        {
            displayNames.AddRange(candidate.AlternateDisplayNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        }

        foreach (var displayName in displayNames)
        {
            raw.Add(displayName);
            var normalizedDisplay = Normalize(displayName);
            var open = normalizedDisplay.IndexOf('(');
            while (open >= 0)
            {
                var close = normalizedDisplay.IndexOf(')', open + 1);
                if (close < 0)
                {
                    break;
                }

                raw.Add(normalizedDisplay[(open + 1)..close]);
                open = normalizedDisplay.IndexOf('(', close + 1);
            }
        }

        var names = new List<string>();
        foreach (var value in raw)
        {
            var normalized = Normalize(value).Trim();
            if (normalized.Length >= MinimumNameLength && !IsShortAsciiLetters(normalized) && !names.Contains(normalized))
            {
                names.Add(normalized);
            }
        }

        return names;
    }

    /// <summary>英字のみで2文字以下の名前か(qa/ui/pm等。一般語・略語と衝突しやすいため照合対象外にする)。</summary>
    private static bool IsShortAsciiLetters(string name) =>
        name.Length <= 2 && name.All(c => c is >= 'a' and <= 'z');

    /// <summary>NFKC正規化(全角英数→半角・半角カナ→全角カナ)し、小文字化する。</summary>
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    /// <summary>正規化済みの本文中に、指示形で登場する指定名があるかを全出現位置について判定する。</summary>
    /// <param name="text">正規化済みの本文。</param>
    /// <param name="name">正規化済みの名前。</param>
    /// <param name="knownNames">全メンバーの正規化済みの名前(「、」区切りの列挙判定に使う)。</param>
    private static bool ContainsNomination(string text, string name, IReadOnlySet<string> knownNames)
    {
        var searchFrom = 0;
        while (searchFrom <= text.Length - name.Length)
        {
            var index = text.IndexOf(name, searchFrom, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            searchFrom = index + 1;

            if (!IsLeftBoundaryOk(text, index, name))
            {
                continue;
            }

            var hasAtPrefix = index > 0 && text[index - 1] == '@';
            var afterIndex = index + name.Length;
            if (IsInstructionForm(text, index, afterIndex, hasAtPrefix, name, knownNames))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>名前の直前の文字が、名前と同じ種類の文字(=別の単語の続き)でないかを判定する。</summary>
    private static bool IsLeftBoundaryOk(string text, int index, string name)
    {
        if (index == 0)
        {
            return true;
        }

        var previousKind = GetCharKind(text[index - 1]);
        return previousKind == CharKind.Other || previousKind != GetCharKind(name[0]);
    }

    /// <summary>名前の直後(または直前の@)が指示形になっているかを判定する。</summary>
    /// <param name="text">正規化済みの本文。</param>
    /// <param name="index">名前の開始位置。</param>
    /// <param name="afterIndex">名前の直後の位置。</param>
    /// <param name="hasAtPrefix">名前の直前が「@」か。</param>
    /// <param name="name">正規化済みの名前(末尾の文字種の判定に使う)。</param>
    /// <param name="knownNames">全メンバーの正規化済みの名前(列挙判定用)。</param>
    private static bool IsInstructionForm(
        string text, int index, int afterIndex, bool hasAtPrefix, string name, IReadOnlySet<string> knownNames)
    {
        var rest = text.AsSpan(afterIndex);

        // 役割宣言形(あなたは〜『〇〇』です / 〇〇として / 担当: 〇〇 / 【〇〇】)。
        if (IsRoleDeclaration(text, index, afterIndex, name))
        {
            return true;
        }

        // 敬称(さん/君 等)が付いている場合は呼びかけとみなす。ただし「〇〇さんの〜」等の言及は除く。
        foreach (var honorific in Honorifics)
        {
            if (rest.StartsWith(honorific, StringComparison.Ordinal))
            {
                var afterHonorific = rest[honorific.Length..];
                return !StartsWithAny(afterHonorific, MentionAfterHonorific);
            }
        }

        // 宛先を示す助詞・記号(に/へ/で/、 等)。「〇〇について」等の言及は除く。
        foreach (var particle in AddresseeParticles)
        {
            if (rest.StartsWith(particle, StringComparison.Ordinal))
            {
                if (particle == "に" && StartsWithAny(rest[particle.Length..], MentionAfterNi))
                {
                    return false;
                }

                // 「ツクル、シラベ、イジワルの3人」のような名前の列挙は指名とみなさない。
                if (ListSeparators.Contains(particle) && IsPartOfNameList(text, index, afterIndex + particle.Length, knownNames))
                {
                    return false;
                }

                return true;
            }
        }

        // 主題の助詞(は/が)は、同じ文に依頼表現がある場合のみ。
        foreach (var particle in TopicParticles)
        {
            if (rest.StartsWith(particle, StringComparison.Ordinal))
            {
                return SentenceContainsRequest(rest[particle.Length..]);
            }
        }

        // 「@名前」形式: 名前の直後が同じ種類の文字で続いていなければ(=名前が途中で切れていなければ)指名とみなす。
        if (hasAtPrefix)
        {
            return rest.IsEmpty || GetCharKind(rest[0]) != GetCharKind(name[^1]) || GetCharKind(rest[0]) == CharKind.Other;
        }

        return false;
    }

    /// <summary>
    /// 命令文によくある役割宣言形になっているかを判定する。
    /// 括弧で囲まれた名前(「〇〇」『〇〇』【〇〇】。閉じ括弧の直後が「の/から/より」の言及は除く)、
    /// 名前の直後の「として/役」、直前の文に「あなたは」があるときの「です/だ/である」、
    /// 直前の「担当:」ラベルのいずれか。
    /// </summary>
    /// <param name="text">正規化済みの本文。</param>
    /// <param name="index">名前の開始位置。</param>
    /// <param name="afterIndex">名前の直後の位置。</param>
    /// <param name="name">正規化済みの名前。</param>
    private static bool IsRoleDeclaration(string text, int index, int afterIndex, string name)
    {
        var rest = text.AsSpan(afterIndex);

        // 括弧囲み(「ツクル」/『ツクル』/【ツクル】)。開きと閉じの種類が対応していること。
        if (index > 0 && !rest.IsEmpty)
        {
            var open = OpenBrackets.IndexOf(text[index - 1]);
            if (open >= 0 && rest[0] == CloseBrackets[open])
            {
                return !StartsWithAny(rest[1..], MentionAfterHonorific);
            }
        }

        if (StartsWithAny(rest, RoleSuffixes))
        {
            return true;
        }

        var beforeName = text.AsSpan(0, index);
        var sentenceStart = beforeName.LastIndexOfAny(SentenceSeparators) + 1;
        var sentenceBefore = beforeName[sentenceStart..].ToString();

        // 「あなたは開発チームのソフトウェアエンジニア『ツクル』です」(括弧なしでも可)。
        if (StartsWithAny(rest, CopulaSuffixes) && SecondPersonMarkers.Any(m => sentenceBefore.Contains(m, StringComparison.Ordinal)))
        {
            return true;
        }

        // 「担当: ツクル」「担当者:ツクル」。
        var trimmedBefore = sentenceBefore.TrimEnd();
        if (trimmedBefore.EndsWith(':'))
        {
            var label = trimmedBefore[..^1].TrimEnd();
            return AssignmentLabels.Any(l => label.EndsWith(l, StringComparison.Ordinal));
        }

        return false;
    }

    /// <summary>
    /// 「、」区切りの名前の列挙の一部か(直後が別のメンバー名で始まる、または直前が「メンバー名+区切り」)を判定する。
    /// </summary>
    /// <param name="text">正規化済みの本文。</param>
    /// <param name="index">対象の名前の開始位置。</param>
    /// <param name="afterSeparator">区切り記号の直後の位置。</param>
    /// <param name="knownNames">全メンバーの正規化済みの名前。</param>
    private static bool IsPartOfNameList(string text, int index, int afterSeparator, IReadOnlySet<string> knownNames)
    {
        var following = text.AsSpan(afterSeparator).TrimStart();
        foreach (var known in knownNames)
        {
            if (following.StartsWith(known, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var preceding = text.AsSpan(0, index).TrimEnd();
        foreach (var separator in ListSeparators)
        {
            if (!preceding.EndsWith(separator, StringComparison.Ordinal))
            {
                continue;
            }

            var beforeSeparator = preceding[..^separator.Length].TrimEnd();
            foreach (var known in knownNames)
            {
                if (beforeSeparator.EndsWith(known, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>文字列が候補のいずれかで始まるか。</summary>
    private static bool StartsWithAny(ReadOnlySpan<char> value, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>先頭から最初の文の区切りまでの範囲に、依頼表現が含まれるか。</summary>
    private static bool SentenceContainsRequest(ReadOnlySpan<char> rest)
    {
        var end = rest.IndexOfAny(SentenceSeparators);
        var sentence = (end < 0 ? rest : rest[..end]).ToString();
        return RequestExpressions.Any(expression => sentence.Contains(expression, StringComparison.Ordinal));
    }

    /// <summary>単語の切れ目判定に使う文字種。</summary>
    private enum CharKind
    {
        Other,
        AsciiWord,
        Hiragana,
        Katakana,
        Han,
    }

    /// <summary>1文字の文字種(英数字・ひらがな・カタカナ・漢字・その他)を返す。</summary>
    private static CharKind GetCharKind(char c)
    {
        if ((c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') || c is '_' or '-')
        {
            return CharKind.AsciiWord;
        }

        if (c is >= 'ぁ' and <= 'ゟ')
        {
            return CharKind.Hiragana;
        }

        if (c is >= '゠' and <= 'ヿ' or >= 'ㇰ' and <= 'ㇿ')
        {
            return CharKind.Katakana;
        }

        if (c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿')
        {
            return CharKind.Han;
        }

        return CharKind.Other;
    }
}
