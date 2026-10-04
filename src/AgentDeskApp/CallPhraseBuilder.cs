namespace AgentDeskApp;

/// <summary>
/// メンバーカードの「呼びかけ文をコピー」でクリップボードへ入れる文を組み立てるクラス。
/// エージェントの正式名(name)を入れることで、リーダー(Claude Code)が確実に該当サブエージェントを
/// 呼び出し、カードが光るようにする。AIやコマンドは一切起動しない(文字列を作るだけ)。
/// </summary>
public static class CallPhraseBuilder
{
    /// <summary>ひな形内で正式名(name / Id)に置換されるプレースホルダー。</summary>
    public const string NamePlaceholder = "{name}";

    /// <summary>ひな形内で表示名に置換されるプレースホルダー。</summary>
    public const string DisplayNamePlaceholder = "{displayName}";

    /// <summary>
    /// 呼びかけ文を組み立てる。表示名が無い(空欄、または正式名と同じ)場合は表示名なし用のひな形を使い、
    /// 「software-engineer エージェント（software-engineer）」のような不自然な重複を避ける。
    /// </summary>
    /// <param name="name">エージェントの正式名(frontmatterのname、Id)。</param>
    /// <param name="displayName">エージェントの表示名(未設定ならnull)。</param>
    /// <param name="template">表示名がある場合のひな形({name}と{displayName}を置換)。</param>
    /// <param name="templateWithoutDisplayName">表示名が無い場合のひな形({name}を置換)。</param>
    /// <returns>置換済みの呼びかけ文。</returns>
    public static string Build(string name, string? displayName, string template, string templateWithoutDisplayName)
    {
        var trimmedName = name.Trim();
        var trimmedDisplayName = displayName?.Trim();
        var hasDisplayName = !string.IsNullOrEmpty(trimmedDisplayName) &&
                             !string.Equals(trimmedDisplayName, trimmedName, StringComparison.Ordinal);

        if (hasDisplayName)
        {
            return template
                .Replace(NamePlaceholder, trimmedName, StringComparison.Ordinal)
                .Replace(DisplayNamePlaceholder, trimmedDisplayName, StringComparison.Ordinal);
        }

        // 表示名なし用のひな形に誤って{displayName}が書かれていても、正式名で埋めて文が壊れないようにする
        return templateWithoutDisplayName
            .Replace(NamePlaceholder, trimmedName, StringComparison.Ordinal)
            .Replace(DisplayNamePlaceholder, trimmedName, StringComparison.Ordinal);
    }

    /// <summary>
    /// エージェント定義と設定値から呼びかけ文を組み立てる。
    /// </summary>
    /// <param name="agent">対象のエージェント定義。</param>
    /// <param name="settings">ひな形を含む設定値。</param>
    /// <returns>置換済みの呼びかけ文。</returns>
    public static string Build(AgentDefinition agent, AppSettings settings) => Build(
        agent.Name,
        agent.DisplayName,
        settings.EffectiveCallPhraseTemplate,
        settings.EffectiveCallPhraseTemplateWithoutDisplayName);
}
