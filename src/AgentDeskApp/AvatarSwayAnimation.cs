using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentDeskApp;

/// <summary>
/// 作業中(Running)のメンバーカードのアバターを、おとなしめに「ゆらゆら」揺らし続ける演出。
/// 下端中央を軸に小さく左右へ傾け、縦に「ぷにぷに」と伸び縮みさせる(伸びる方向のみ。倍率は常に1以上)。上下の位置移動はしない
/// (アバター画像に背景まで描かれているため、ユーザー決定)。
/// 傾けるとカードの上の角に隙間が見えるので、揺れている間だけ隙間を覆う分だけ少し拡大しておく。
/// 状態監視の周期ごとに呼ばれても作り直さないよう、揺れている要素を覚えておき二重に開始しない。
/// </summary>
public static class AvatarSwayAnimation
{
    /// <summary>拡大(隙間隠し)をかけたり外したりするときの切り替え時間(ミリ秒)。見た目の滑らかさのための固定値。</summary>
    private const int CoverTransitionMilliseconds = 600;

    /// <summary>揺れを止めるとき、元の姿勢へ戻すのにかける時間(ミリ秒)。見た目の滑らかさのための固定値。</summary>
    private const int SettleMilliseconds = 450;

    /// <summary>いま揺れているアバター要素(要素が破棄されたら自動的に消える弱参照の表)。</summary>
    private static readonly ConditionalWeakTable<FrameworkElement, object> Swaying = new();

    /// <summary>アバター要素がいま揺れているかどうかを返す。</summary>
    /// <param name="avatar">アバター要素。</param>
    /// <returns>揺れていればtrue。</returns>
    public static bool IsSwaying(FrameworkElement avatar) => Swaying.TryGetValue(avatar, out _);

    /// <summary>
    /// 傾けてもカードの四隅に隙間が見えないために必要な拡大率を計算する(下端中央を軸に傾ける前提)。
    /// 傾けたときに上の角が最も外へずれるため、横方向で「cos(角度) + 2 × (縦÷横) × sin(角度)」倍あれば覆える。
    /// 下の角に出るわずかな隙間(横幅の半分 × sin(角度)、メンバーカードで1.5度なら約3px)はカード下部の暗いグラデーションに隠れる。
    /// 縦の伸び縮み(Breath)は上方向に伸びるだけ(倍率1以上)なので隙間は増えず、この拡大率に含める必要はない。
    /// </summary>
    /// <param name="angleDegrees">最大の傾き(度)。</param>
    /// <param name="heightToWidthRatio">アバターの表示領域の縦÷横。</param>
    /// <returns>必要な拡大率(1以上)。</returns>
    public static double ComputeCoverScale(double angleDegrees, double heightToWidthRatio)
    {
        var radians = Math.Abs(angleDegrees) * Math.PI / 180.0;
        var scale = Math.Cos(radians) + (2.0 * Math.Max(0, heightToWidthRatio) * Math.Sin(radians));
        return Math.Max(1.0, scale);
    }

    /// <summary>
    /// 揺れを開始する。すでに揺れている要素なら何もしない(周期ごとの呼び出しでカクつかないようにするため)。
    /// </summary>
    /// <param name="avatar">揺らすアバター要素。</param>
    /// <param name="angleDegrees">左右に傾く最大角度(度)。</param>
    /// <param name="periodMilliseconds">1往復(右→左→元)の時間(ミリ秒)。</param>
    /// <param name="breathAmplitude">縦の伸び縮みの強さ(倍率の増分。0なら伸び縮みしない)。</param>
    /// <param name="heightToWidthRatio">アバターの表示領域の縦÷横(隙間を覆う拡大率の計算に使う)。</param>
    public static void Start(FrameworkElement avatar, double angleDegrees, int periodMilliseconds, double breathAmplitude, double heightToWidthRatio)
    {
        if (IsSwaying(avatar))
        {
            return;
        }

        Swaying.AddOrUpdate(avatar, new object());
        var parts = AvatarTransformParts.Ensure(avatar);
        var period = TimeSpan.FromMilliseconds(periodMilliseconds);

        // 1) 隙間を覆うための拡大を、なめらかにかける(止めるまで保持する)
        var cover = ComputeCoverScale(angleDegrees, heightToWidthRatio);
        var coverAnim = new DoubleAnimation(cover, TimeSpan.FromMilliseconds(CoverTransitionMilliseconds))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        parts.Cover.BeginAnimation(ScaleTransform.ScaleXProperty, coverAnim);
        parts.Cover.BeginAnimation(ScaleTransform.ScaleYProperty, coverAnim);

        // 2) 左右の傾き: 0 → 右 → 0 → 左 → 0 をくり返す(0から始めるので開始時に姿勢が飛ばない)
        var tilt = new DoubleAnimationUsingKeyFrames { Duration = period, RepeatBehavior = RepeatBehavior.Forever };
        (double Time, double Angle)[] tiltKeys = [(0.0, 0), (0.25, angleDegrees), (0.5, 0), (0.75, -angleDegrees), (1.0, 0)];
        foreach (var (time, angle) in tiltKeys)
        {
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(
                angle,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(periodMilliseconds * time)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        }
        parts.Tilt.BeginAnimation(RotateTransform.AngleProperty, tilt);

        // 3) 縦の伸び縮み(ぷにぷに): 傾きが左右の端に来るたびに少し伸びる(1往復で2回)
        if (breathAmplitude > 0)
        {
            var breath = new DoubleAnimation(1.0, 1.0 + breathAmplitude, TimeSpan.FromMilliseconds(periodMilliseconds / 4.0))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            parts.Breath.BeginAnimation(ScaleTransform.ScaleYProperty, breath);
        }
    }

    /// <summary>
    /// 揺れを止めて元の姿勢(傾き0・等倍)に戻す。揺れていない要素なら何もしない。
    /// </summary>
    /// <param name="avatar">止めるアバター要素。</param>
    /// <param name="smooth">trueならなめらかに戻す。falseなら即座に戻す(カードが画面から外れたとき等)。</param>
    public static void Stop(FrameworkElement avatar, bool smooth)
    {
        if (!Swaying.Remove(avatar))
        {
            return;
        }

        var parts = AvatarTransformParts.Ensure(avatar);
        if (!smooth)
        {
            ClearAll(parts);
            return;
        }

        // 現在の値から元の値へなめらかに戻し、戻し終わったらアニメーションを外す。
        // 戻している途中で再び揺れ始めた場合は、新しい揺れを消してしまわないよう何もしない。
        var settle = TimeSpan.FromMilliseconds(SettleMilliseconds);
        var ease = new SineEase { EasingMode = EasingMode.EaseOut };
        var toZero = new DoubleAnimation(0, settle) { EasingFunction = ease };
        var toOne = new DoubleAnimation(1, settle) { EasingFunction = ease };
        var breathToOne = toOne.Clone();
        var coverXToOne = toOne.Clone();
        toOne.Completed += (_, _) =>
        {
            if (!IsSwaying(avatar))
            {
                ClearAll(parts);
            }
        };

        parts.Tilt.BeginAnimation(RotateTransform.AngleProperty, toZero);
        parts.Breath.BeginAnimation(ScaleTransform.ScaleYProperty, breathToOne);
        parts.Cover.BeginAnimation(ScaleTransform.ScaleXProperty, coverXToOne);
        parts.Cover.BeginAnimation(ScaleTransform.ScaleYProperty, toOne);
    }

    /// <summary>揺れ関係のアニメーションをすべて外し、元の値(傾き0・等倍)を見せる。</summary>
    /// <param name="parts">アバター要素の変形の組。</param>
    private static void ClearAll(AvatarTransformParts parts)
    {
        parts.Tilt.BeginAnimation(RotateTransform.AngleProperty, null);
        parts.Breath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        parts.Cover.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        parts.Cover.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }
}
