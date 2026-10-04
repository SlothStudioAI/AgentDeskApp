namespace AgentDeskApp;

/// <summary>
/// メンバーカード左上のエンジンバッジに付けるツールチップ文言と、エンジン種別との対応(UIから分離して単体テスト可能にする)。
/// </summary>
public static class MemberBadgeToolTip
{
    /// <summary>Claude専用メンバーのバッジに付けるツールチップ文言。</summary>
    public const string MemberClaudeBadgeToolTip = "Claude に配備されています";

    /// <summary>Gemini専用メンバーのバッジに付けるツールチップ文言。</summary>
    public const string MemberGeminiBadgeToolTip = "Gemini に配備されています";

    /// <summary>Shared(両エンジン配備)メンバーのバッジに付けるツールチップ文言。</summary>
    public const string MemberSharedBadgeToolTip = "Claude と Gemini の両方に配備されています";

    /// <summary>
    /// エンジン種別に対応するツールチップ文言を返す。
    /// </summary>
    /// <param name="engine">メンバーのエンジン種別。</param>
    /// <returns>ツールチップ文言(未知の種別は Claude 扱い)。</returns>
    public static string For(AgentEngineKind engine) => engine switch
    {
        AgentEngineKind.Gemini => MemberGeminiBadgeToolTip,
        AgentEngineKind.Shared => MemberSharedBadgeToolTip,
        _ => MemberClaudeBadgeToolTip,
    };
}
