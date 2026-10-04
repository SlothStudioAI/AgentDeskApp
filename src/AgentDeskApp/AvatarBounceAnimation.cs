using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentDeskApp;

/// <summary>
/// アバター要素に付ける変形(拡大縮小・回転)の組。完了時の「ぷるん」と作業中の「ゆらゆら」が
/// 互いの値を上書きしないよう、役割ごとに別の変形を重ねて持つ。基準点はすべて下端中央。
/// </summary>
/// <param name="Bounce">完了時の「ぷるん」用の拡大縮小。</param>
/// <param name="Cover">揺れで傾いたときに四隅の隙間が見えないよう、作業中だけ少し拡大しておくための拡大縮小。</param>
/// <param name="Breath">作業中のごく小さな縦の伸び縮み用の拡大縮小。</param>
/// <param name="Tilt">作業中の左右の傾き用の回転。</param>
public sealed record AvatarTransformParts(ScaleTransform Bounce, ScaleTransform Cover, ScaleTransform Breath, RotateTransform Tilt)
{
    /// <summary>
    /// アバター要素の変形の組を取得する。まだ付いていなければ作って付け、基準点を下端中央にする。
    /// </summary>
    /// <param name="avatar">アバター要素(画像、または画像が無い場合のイニシャル文字)。</param>
    /// <returns>アバター要素に付いている変形の組。</returns>
    public static AvatarTransformParts Ensure(FrameworkElement avatar)
    {
        if (avatar.RenderTransform is TransformGroup { IsFrozen: false } group &&
            group.Children.Count == 4 &&
            group.Children[0] is ScaleTransform bounce &&
            group.Children[1] is ScaleTransform cover &&
            group.Children[2] is ScaleTransform breath &&
            group.Children[3] is RotateTransform tilt)
        {
            return new AvatarTransformParts(bounce, cover, breath, tilt);
        }

        var parts = new AvatarTransformParts(new ScaleTransform(1, 1), new ScaleTransform(1, 1), new ScaleTransform(1, 1), new RotateTransform(0));
        var newGroup = new TransformGroup();
        newGroup.Children.Add(parts.Bounce);
        newGroup.Children.Add(parts.Cover);
        newGroup.Children.Add(parts.Breath);
        newGroup.Children.Add(parts.Tilt);
        avatar.RenderTransform = newGroup;

        // 下端中央を基準にする(足元は動かさない)
        avatar.RenderTransformOrigin = new Point(0.5, 1.0);
        return parts;
    }
}

/// <summary>
/// 完了したメンバーカードのアバターを、その場で「ぷるん」と伸び縮みさせる演出。
/// 下端中央を基準に「横に広がって縦に縮む→縦に伸びる→小さく揺れ戻す」を指定回数くり返す。
/// アバター画像は背景まで描かれているため、上下に跳ねる(位置の移動)は行わない(ユーザー決定)。
/// また、縮んだときにカード上部へ隙間が見えないよう、倍率は常に1以上に保ち、はみ出た分はカードの
/// クリップ(ClipToBounds)で隠す。
/// </summary>
public static class AvatarBounceAnimation
{
    /// <summary>
    /// アバター要素に「ぷるん」演出を再生する。再生中に再度呼ばれた場合は最初からやり直す。
    /// </summary>
    /// <param name="avatar">伸び縮みさせるアバター要素(画像、または画像が無い場合のイニシャル文字)。</param>
    /// <param name="durationMilliseconds">「ぷるん」1回分の長さ(ミリ秒)。</param>
    /// <param name="amplitude">伸び縮みの強さ(倍率の増分。0.22なら最大22%横に広がる)。</param>
    /// <param name="repeatCount">くり返す回数(1以上)。</param>
    public static void Play(FrameworkElement avatar, int durationMilliseconds, double amplitude, int repeatCount)
    {
        var scale = AvatarTransformParts.Ensure(avatar).Bounce;
        var count = Math.Max(1, repeatCount);
        var total = TimeSpan.FromMilliseconds((double)durationMilliseconds * count);

        // 1回分の各キー: (時間の割合, 横の増分の割合, 縦の増分の割合)。
        // 縦に縮む表現は「横だけ広げて縦は等倍」、縦に伸びる表現は「縦だけ伸ばして横は等倍」とし、
        // 倍率を1未満にしない(上部に隙間を出さない)ことで相対的な伸び縮みを表現する。
        (double Time, double X, double Y)[] keys =
        [
            (0.00, 0.00, 0.00),
            (0.15, 1.00, 0.00), // 横に広がって縦に縮む(つぶれる)
            (0.38, 0.00, 0.80), // 縦に伸びる
            (0.58, 0.40, 0.00), // 小さく揺れ戻す
            (0.78, 0.00, 0.20),
            (1.00, 0.00, 0.00),
        ];

        var animX = new DoubleAnimationUsingKeyFrames { Duration = total };
        var animY = new DoubleAnimationUsingKeyFrames { Duration = total };
        for (var round = 0; round < count; round++)
        {
            foreach (var (time, x, y) in keys)
            {
                // 2回目以降の開始キー(0.00)は前回の終了キー(1.00)と同じ時刻・同じ値なので省く
                if (round > 0 && time == 0.0)
                {
                    continue;
                }

                var keyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMilliseconds * (round + time)));
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                animX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0 + (amplitude * x), keyTime, ease));
                animY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0 + (amplitude * y), keyTime, ease));
            }
        }

        // 終了後は等倍に戻す(アニメーションの保持値を外して元の値1.0を見せる)
        animY.Completed += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }
}
