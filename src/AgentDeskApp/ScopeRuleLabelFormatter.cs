namespace AgentDeskApp;

/// <summary>
/// ルールボタンの表示文言を決めるときのスコープ種別。
/// (MainWindow 内の private な ScopeKind を、純粋ロジック側から参照できるようにした公開版)
/// </summary>
public enum ScopeRuleKind
{
    /// <summary>Dashboard(全体)。</summary>
    Department,
    /// <summary>ワークスペース(グループ)。</summary>
    Group,
    /// <summary>チーム。</summary>
    Team
}

/// <summary>
/// TEAM/WORKSPACE パネルのルールボタン(ManageTeamButton)の文言を、選択中のスコープから組み立てるクラス。
/// 例: ワークスペース「Sloth Studio」→「Sloth Studio ワークスペースのルール」、Dashboard →「ルール」。
/// ルール管理ウィンドウの見出しも同じ文言を使う(共通化)。
/// </summary>
public static class ScopeRuleLabelFormatter
{
    /// <summary>ワークスペースの名前の後ろに付ける接尾語。</summary>
    public const string GroupSuffix = " ワークスペースのルール";

    /// <summary>チームの名前の後ろに付ける接尾語。</summary>
    public const string TeamSuffix = " チームのルール";

    /// <summary>Dashboard(全体)選択時、または名前が空のときの文言。</summary>
    public const string DefaultLabel = "ルール";

    /// <summary>
    /// ボタン文言を組み立てる。
    /// </summary>
    /// <param name="kind">選択中のスコープ種別。</param>
    /// <param name="name">ワークスペース名またはチーム名。Dashboard では使わない。</param>
    /// <returns>Dashboard または名前が空なら「ルール」、それ以外はワークスペースは「{name} ワークスペースのルール」、チームは「{name} チームのルール」。</returns>
    public static string Format(ScopeRuleKind kind, string? name)
    {
        if (kind == ScopeRuleKind.Department || string.IsNullOrWhiteSpace(name))
        {
            return DefaultLabel;
        }
        return name.Trim() + (kind == ScopeRuleKind.Team ? TeamSuffix : GroupSuffix);
    }
}
