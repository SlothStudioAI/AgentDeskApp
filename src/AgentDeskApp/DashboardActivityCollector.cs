namespace AgentDeskApp;

/// <summary>
/// Dashboard(全チーム横断)の「稼働中・完了のメンバー」エリアに出すメンバーを決めるクラス(画面部品に依存しない判定ロジック)。
/// 表示する条件は次のとおり。
/// - 作業中(Running): 作業が終わるまで出し続ける。
/// - 完了(Done): 完了カードをクリックして確認するまで出し続ける(確認すると消える)。
///   「確認済みか」は <see cref="MemberCardAttentionTracker"/>(完了の光を管理する既存の仕組み)の
///   「完了未確認」をそのまま使うため、他のチームページでクリックして光が消えた場合もここから消える。
/// メンバーの識別はId(同じIdは1人として数える)。
/// </summary>
public static class DashboardActivityCollector
{
    /// <summary>Dashboardの名称(サイドバー・ページ上部の見出し・共通メンバーの所属ラベルに共通で使う)。</summary>
    public const string DashboardName = "Dashboard";

    /// <summary>ページ上部の小見出し(大文字表記)。</summary>
    public const string BannerKindLabel = "DASHBOARD";

    /// <summary>ページ上部のサブタイトル(簡潔な説明)。</summary>
    public const string BannerSubtitle = "稼働中・完了したメンバーと共通メンバー";

    /// <summary>「稼働中・完了のメンバー」エリアの見出し。</summary>
    public const string ActiveSectionTitle = "⚡ 稼働中・完了のメンバー";

    /// <summary>稼働中・完了が0件のときに出す空表示の文言。</summary>
    public const string EmptyMessage = "稼働中・完了したメンバーはいません";

    /// <summary>下部の共通メンバー一覧の見出し。</summary>
    public const string CommonMembersSectionTitle = "📋 共通メンバー";

    /// <summary>所属ラベルの区切り文字(「ワークスペース / チーム」の形式)。</summary>
    private const string ScopeLabelSeparator = " / ";

    /// <summary>
    /// Dashboardの上部エリアに出すメンバーを選ぶ。同じIdは最初の1件だけを採用し、表示名順に並べる。
    /// </summary>
    /// <param name="entries">全チーム横断のメンバー一覧(所属ラベル付き)。</param>
    /// <param name="getState">Idから現在の稼働状態を返す関数。</param>
    /// <param name="isAwaitingAcknowledgement">Idが「完了未確認」(クリックでの確認待ち)かどうかを返す関数。</param>
    /// <returns>上部エリアに出すメンバー(0件なら空)。</returns>
    public static IReadOnlyList<StudioAgentEntry> Collect(
        IEnumerable<StudioAgentEntry> entries,
        Func<string, MemberActivityState> getState,
        Func<string, bool> isAwaitingAcknowledgement)
    {
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Agent.Name))
            .DistinctBy(e => e.Agent.Name, StringComparer.Ordinal)
            .Where(e => IsVisible(getState(e.Agent.Name), isAwaitingAcknowledgement(e.Agent.Name)))
            .OrderBy(e => e.Agent.EffectiveDisplayName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 1人のメンバーを上部エリアに出すかを返す。作業中なら常に出し、完了は未確認のときだけ出す。
    /// </summary>
    /// <param name="state">現在の稼働状態。</param>
    /// <param name="awaitingAcknowledgement">完了未確認(クリックでの確認待ち)かどうか。</param>
    /// <returns>出すべきならtrue。</returns>
    public static bool IsVisible(MemberActivityState state, bool awaitingAcknowledgement) => state switch
    {
        MemberActivityState.Running => true,
        MemberActivityState.Done => awaitingAcknowledgement,
        _ => false,
    };

    /// <summary>空表示(「稼働中・完了したメンバーはいません」)を出すべきかを返す。</summary>
    /// <param name="visibleCount">上部エリアに出すメンバーの人数。</param>
    /// <returns>0件ならtrue。</returns>
    public static bool ShouldShowEmptyMessage(int visibleCount) => visibleCount <= 0;

    /// <summary>
    /// カードに小さく出す所属チーム名を、所属ラベルから作る。「ワークスペース / チーム」形式ならチーム名だけにし、
    /// ワークスペース直下・共通のメンバーはそのままのラベルを使う。
    /// </summary>
    /// <param name="scopeLabel">所属先の表示ラベル(例: "Sloth Studio / AgentDeskApp")。</param>
    /// <returns>カードに出すチーム名。</returns>
    public static string FormatTeamLabel(string scopeLabel)
    {
        var index = scopeLabel.LastIndexOf(ScopeLabelSeparator, StringComparison.Ordinal);
        return index < 0 ? scopeLabel : scopeLabel[(index + ScopeLabelSeparator.Length)..];
    }
}
