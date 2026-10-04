using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AgentDeskApp;

/// <summary>
/// 完了したメンバーカードを「はっきり分かる光り方」で光らせ続ける演出。
/// カードの内側に太めの光る枠を重ね、カードの外側にもぼんやりした光(影の効果)を出す。
/// フェードで消さず、ユーザーがクリックして確認するまで光らせ続ける(ゆっくり明滅させてもよい)。
/// 枠の太さを変えるとカードの中身の配置がずれるため、太い枠はカードの枠線ではなく、
/// 中身の上に重ねた枠用の要素(当たり判定なし)で表す。
/// </summary>
public static class CardGlowEffect
{
    /// <summary>明滅したときの最も暗い瞬間の不透明度(1が最も明るい)。見た目の調整用の固定値。</summary>
    private const double PulseMinimumOpacity = 0.45;

    /// <summary>いま光っているカード(カードが破棄されたら自動的に消える弱参照の表)。</summary>
    private static readonly ConditionalWeakTable<Border, object> Glowing = new();

    /// <summary>カードがいま光っているかどうかを返す。</summary>
    /// <param name="card">メンバーカード。</param>
    /// <returns>光っていればtrue。</returns>
    public static bool IsShown(Border card) => Glowing.TryGetValue(card, out _);

    /// <summary>
    /// カードを光らせる。すでに光っているカードなら何もしない(状態監視の周期ごとに作り直さないため)。
    /// </summary>
    /// <param name="card">光らせるメンバーカード。</param>
    /// <param name="frame">カードの中身の上に重ねた枠用の要素(普段は非表示)。</param>
    /// <param name="color">光の色。</param>
    /// <param name="thickness">枠の太さ(ピクセル)。</param>
    /// <param name="blurRadius">外側のぼんやりした光の広がり(0なら外側の光なし)。</param>
    /// <param name="pulseMilliseconds">明滅の周期(ミリ秒、明→暗→明)。0なら明滅しない。</param>
    public static void Show(Border card, Border frame, Color color, double thickness, double blurRadius, int pulseMilliseconds)
    {
        if (IsShown(card))
        {
            return;
        }

        Glowing.AddOrUpdate(card, new object());

        frame.BorderBrush = new SolidColorBrush(color);
        frame.BorderThickness = new Thickness(thickness);
        frame.Visibility = Visibility.Visible;
        card.BorderBrush = new SolidColorBrush(color);

        DropShadowEffect? halo = null;
        if (blurRadius > 0)
        {
            halo = new DropShadowEffect
            {
                Color = color,
                ShadowDepth = 0,
                BlurRadius = blurRadius,
                Opacity = 1.0,
            };
            card.Effect = halo;
        }

        if (pulseMilliseconds > 0)
        {
            // 明→暗→明 で1周期。AutoReverseで戻るため片道は周期の半分。
            var pulse = new DoubleAnimation(1.0, PulseMinimumOpacity, TimeSpan.FromMilliseconds(pulseMilliseconds / 2.0))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            frame.BeginAnimation(UIElement.OpacityProperty, pulse);
            halo?.BeginAnimation(DropShadowEffect.OpacityProperty, pulse);
        }
    }

    /// <summary>
    /// カードの光を消す。光っていないカードなら何もしない。カードの枠線の色は呼び出し側で元に戻す。
    /// </summary>
    /// <param name="card">光を消すメンバーカード。</param>
    /// <param name="frame">カードの中身の上に重ねた枠用の要素。</param>
    public static void Hide(Border card, Border frame)
    {
        if (!Glowing.Remove(card))
        {
            return;
        }

        frame.BeginAnimation(UIElement.OpacityProperty, null);
        frame.Visibility = Visibility.Collapsed;
        if (card.Effect is DropShadowEffect halo)
        {
            halo.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        }
        card.Effect = null;
    }
}
