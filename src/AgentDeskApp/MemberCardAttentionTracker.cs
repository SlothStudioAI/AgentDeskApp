namespace AgentDeskApp;

/// <summary>メンバーカードの状態バッジに出す表示の種類。完了(Done)は光で知らせるためバッジには出さない(ユーザー決定)。</summary>
public enum MemberCardBadgeKind
{
    /// <summary>待機中。完了後(光っている間も含む)もこの表示にする。</summary>
    Idle,

    /// <summary>作業中。</summary>
    Active,

    /// <summary>ユーザー操作による中断。</summary>
    Cancelled,

    /// <summary>タイムアウト。</summary>
    TimedOut,
}

/// <summary>
/// メンバーカードの「完了を知らせる光」を出すかどうかを管理するクラス(画面部品に依存しない判定ロジック)。
/// 状態監視の周期ごとに <see cref="Observe"/> で最新の状態を渡すと、作業中→完了(Running→Done)を検知して
/// 「完了未確認」として覚え、ユーザーがカードをクリックする(<see cref="Acknowledge"/>)まで光らせ続ける。
/// 完了未確認の情報はメモリ上だけで持ち、アプリを再起動すると消える(仕様)。
/// </summary>
public sealed class MemberCardAttentionTracker
{
    /// <summary>メンバー名ごとの、直近の周期で観測した状態。</summary>
    private readonly Dictionary<string, MemberActivityState> _lastKnownStates = new(StringComparer.Ordinal);

    /// <summary>完了したがまだクリックで確認されていない(光らせ続ける)メンバー名。</summary>
    private readonly HashSet<string> _awaitingAcknowledgement = new(StringComparer.Ordinal);

    /// <summary>
    /// 最新の状態を記録し、作業中→完了の切り替わり(完了した瞬間)かどうかを返す。
    /// 完了した瞬間なら「完了未確認」に加える。再び作業中になった場合は、前回の完了は確認済み扱いにして光を消す
    /// (新しい作業を頼んだ時点で前回の結果は見たとみなす)。
    /// </summary>
    /// <param name="memberName">メンバー名(エージェントのId)。</param>
    /// <param name="state">現在の稼働状態。</param>
    /// <returns>この呼び出しで Running→Done を検知した場合はtrue。</returns>
    public bool Observe(string memberName, MemberActivityState state)
    {
        var justCompleted = _lastKnownStates.TryGetValue(memberName, out var previous) &&
                            previous == MemberActivityState.Running &&
                            state == MemberActivityState.Done;
        _lastKnownStates[memberName] = state;

        if (justCompleted)
        {
            _awaitingAcknowledgement.Add(memberName);
        }
        else if (state == MemberActivityState.Running)
        {
            _awaitingAcknowledgement.Remove(memberName);
        }

        return justCompleted;
    }

    /// <summary>指定メンバーが「完了未確認」(カードを光らせる状態)かどうかを返す。</summary>
    /// <param name="memberName">メンバー名(エージェントのId)。</param>
    /// <returns>光らせるべきならtrue。</returns>
    public bool IsAwaitingAcknowledgement(string memberName) => _awaitingAcknowledgement.Contains(memberName);

    /// <summary>
    /// カードのクリックで完了を確認済みにする。光っていたカードのクリックは「光を消す」ためだけに使い、
    /// 詳細画面は開かない(呼び出し側でクリックを消費する)。
    /// </summary>
    /// <param name="memberName">メンバー名(エージェントのId)。</param>
    /// <returns>光っていて、今回のクリックで消した場合はtrue(=クリックを消費する)。光っていなければfalse。</returns>
    public bool Acknowledge(string memberName) => _awaitingAcknowledgement.Remove(memberName);

    /// <summary>
    /// 稼働状態から、メンバーカードの状態バッジに出す表示を決める。完了(Done)は光で知らせるため Idle 扱いにする。
    /// </summary>
    /// <param name="state">稼働状態。</param>
    /// <returns>バッジの表示の種類。</returns>
    public static MemberCardBadgeKind GetBadgeKind(MemberActivityState state) => state switch
    {
        MemberActivityState.Running => MemberCardBadgeKind.Active,
        MemberActivityState.Cancelled => MemberCardBadgeKind.Cancelled,
        MemberActivityState.TimedOut => MemberCardBadgeKind.TimedOut,
        _ => MemberCardBadgeKind.Idle,
    };

    /// <summary>作業中の「ゆらゆら」を動かすべきかを返す。</summary>
    /// <param name="state">稼働状態。</param>
    /// <param name="swayEnabled">設定で作業中の揺れがオンかどうか。</param>
    /// <returns>Running かつ設定がオンならtrue。</returns>
    public static bool ShouldSway(MemberActivityState state, bool swayEnabled) =>
        swayEnabled && state == MemberActivityState.Running;
}
