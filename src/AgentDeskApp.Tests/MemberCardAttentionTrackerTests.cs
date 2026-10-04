using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="MemberCardAttentionTracker"/>(完了したカードを光らせる・クリックで消す・バッジの表示)と、
/// 作業中の揺れで隙間を覆う拡大率の計算(<see cref="AvatarSwayAnimation.ComputeCoverScale"/>)に関するテスト。
/// アニメーションそのものは実機で確認する。
/// </summary>
public class MemberCardAttentionTrackerTests
{
    [Fact]
    public void Observe_作業中から完了に変わった瞬間だけtrueを返し光らせる()
    {
        var tracker = new MemberCardAttentionTracker();

        Assert.False(tracker.Observe("tsukuru", MemberActivityState.Running));
        Assert.True(tracker.Observe("tsukuru", MemberActivityState.Done));
        Assert.True(tracker.IsAwaitingAcknowledgement("tsukuru"));

        // 次の周期も Done のままなら、完了の瞬間ではない(ぷるん・通知は1回だけ)が光は続く
        Assert.False(tracker.Observe("tsukuru", MemberActivityState.Done));
        Assert.True(tracker.IsAwaitingAcknowledgement("tsukuru"));
    }

    [Fact]
    public void Observe_初めて見たときにDoneでも光らせない()
    {
        // アプリ起動時点ですでに完了していたメンバーは、今回の完了ではないので光らせない
        var tracker = new MemberCardAttentionTracker();

        Assert.False(tracker.Observe("tsukuru", MemberActivityState.Done));
        Assert.False(tracker.IsAwaitingAcknowledgement("tsukuru"));
    }

    [Theory]
    [InlineData(MemberActivityState.Idle)]
    [InlineData(MemberActivityState.Cancelled)]
    [InlineData(MemberActivityState.TimedOut)]
    public void Observe_作業中から完了以外に変わっても光らせない(MemberActivityState next)
    {
        var tracker = new MemberCardAttentionTracker();

        tracker.Observe("tsukuru", MemberActivityState.Running);

        Assert.False(tracker.Observe("tsukuru", next));
        Assert.False(tracker.IsAwaitingAcknowledgement("tsukuru"));
    }

    [Fact]
    public void Acknowledge_光っているカードのクリックで光が消えクリックを消費する()
    {
        var tracker = new MemberCardAttentionTracker();
        tracker.Observe("tsukuru", MemberActivityState.Running);
        tracker.Observe("tsukuru", MemberActivityState.Done);

        Assert.True(tracker.Acknowledge("tsukuru"));
        Assert.False(tracker.IsAwaitingAcknowledgement("tsukuru"));

        // 光が消えた後(状態は Done のまま)の周期でも、また光り出さない
        Assert.False(tracker.Observe("tsukuru", MemberActivityState.Done));
        Assert.False(tracker.IsAwaitingAcknowledgement("tsukuru"));
    }

    [Fact]
    public void Acknowledge_光っていないカードのクリックは消費しない()
    {
        // false が返る = 従来どおり詳細画面を開く
        var tracker = new MemberCardAttentionTracker();

        Assert.False(tracker.Acknowledge("tsukuru"));

        tracker.Observe("tsukuru", MemberActivityState.Running);
        Assert.False(tracker.Acknowledge("tsukuru"));
    }

    [Fact]
    public void 光るのは完了したメンバーだけで他のメンバーには影響しない()
    {
        var tracker = new MemberCardAttentionTracker();
        tracker.Observe("tsukuru", MemberActivityState.Running);
        tracker.Observe("shirabe", MemberActivityState.Running);

        tracker.Observe("tsukuru", MemberActivityState.Done);
        tracker.Observe("shirabe", MemberActivityState.Running);

        Assert.True(tracker.IsAwaitingAcknowledgement("tsukuru"));
        Assert.False(tracker.IsAwaitingAcknowledgement("shirabe"));
        Assert.False(tracker.Acknowledge("shirabe"));
        Assert.True(tracker.IsAwaitingAcknowledgement("tsukuru"));
    }

    [Fact]
    public void 光っている間に再び作業を始めたら光は消え_また完了したら光る()
    {
        var tracker = new MemberCardAttentionTracker();
        tracker.Observe("tsukuru", MemberActivityState.Running);
        tracker.Observe("tsukuru", MemberActivityState.Done);

        tracker.Observe("tsukuru", MemberActivityState.Running);
        Assert.False(tracker.IsAwaitingAcknowledgement("tsukuru"));

        Assert.True(tracker.Observe("tsukuru", MemberActivityState.Done));
        Assert.True(tracker.IsAwaitingAcknowledgement("tsukuru"));
    }

    [Theory]
    [InlineData(MemberActivityState.Idle, MemberCardBadgeKind.Idle)]
    [InlineData(MemberActivityState.Running, MemberCardBadgeKind.Active)]
    [InlineData(MemberActivityState.Done, MemberCardBadgeKind.Idle)] // Done はバッジに出さない(光で分かるため)
    [InlineData(MemberActivityState.Cancelled, MemberCardBadgeKind.Cancelled)]
    [InlineData(MemberActivityState.TimedOut, MemberCardBadgeKind.TimedOut)]
    public void GetBadgeKind_DoneはIdle表示になりActiveは残る(MemberActivityState state, MemberCardBadgeKind expected)
    {
        Assert.Equal(expected, MemberCardAttentionTracker.GetBadgeKind(state));
    }

    [Fact]
    public void ShouldSway_作業中かつ設定オンのときだけ揺れる()
    {
        Assert.True(MemberCardAttentionTracker.ShouldSway(MemberActivityState.Running, swayEnabled: true));
        Assert.False(MemberCardAttentionTracker.ShouldSway(MemberActivityState.Running, swayEnabled: false));
        Assert.False(MemberCardAttentionTracker.ShouldSway(MemberActivityState.Done, swayEnabled: true));
        Assert.False(MemberCardAttentionTracker.ShouldSway(MemberActivityState.Idle, swayEnabled: true));
    }

    [Fact]
    public void ComputeCoverScale_傾けても上の角に隙間が出ない拡大率になる()
    {
        // カード 236×364、既定の1度: cos(1°) + 2 × (364/236) × sin(1°) ≒ 1.054
        var scale = AvatarSwayAnimation.ComputeCoverScale(1.0, 364.0 / 236.0);
        Assert.InRange(scale, 1.05, 1.06);

        // 上の角(横幅の半分, 高さ)を逆回転させた位置が、拡大後の画像の横幅の半分に収まることを確かめる
        var radians = Math.PI / 180.0;
        var halfWidth = 236.0 / 2;
        var cornerX = (halfWidth * Math.Cos(radians)) + (364.0 * Math.Sin(radians));
        Assert.True(cornerX <= (halfWidth * scale) + 1e-9);

        // 傾けない場合は拡大しない
        Assert.Equal(1.0, AvatarSwayAnimation.ComputeCoverScale(0, 364.0 / 236.0));
    }

    [Theory]
    [InlineData(236.0, 364.0)] // メンバーカード
    [InlineData(260.0, 400.0)] // リーダーカード(2026-10-03 縦長カードに変更)
    public void ComputeCoverScale_既定の傾きでも上の角に隙間が出ない(double width, double height)
    {
        // 既定値(1.5度)で、上の角を逆回転させた位置が拡大後の画像の横幅・高さに収まることを確かめる。
        // 縦の伸び縮みは上方向へ伸びるだけ(倍率1以上)なので、ここでは伸びていない状態(最も厳しい状態)で確認する。
        var angle = AppSettings.DefaultRunningSwayAngleDegrees;
        var scale = AvatarSwayAnimation.ComputeCoverScale(angle, height / width);
        var radians = angle * Math.PI / 180.0;
        var halfWidth = width / 2;

        var cornerX = (halfWidth * Math.Cos(radians)) + (height * Math.Sin(radians));
        var cornerY = (height * Math.Cos(radians)) + (halfWidth * Math.Sin(radians));
        Assert.True(cornerX <= (halfWidth * scale) + 1e-9);
        Assert.True(cornerY <= (height * scale) + 1e-9);
    }
}
