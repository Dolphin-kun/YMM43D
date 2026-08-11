using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// <see cref="Animation"/> の値を取り出すのに必要な時間情報の組。
    /// </summary>
    /// <remarks>
    /// YMM4 の <c>Animation.GetValue(frame, length, fps)</c> は3つの引数を常に
    /// セットで要求します。この3つ組をあちこちで引き回していたため、1つの型にまとめました。
    /// </remarks>
    /// <param name="Frame">現在のフレーム位置。</param>
    /// <param name="Length">全体の長さ（フレーム数）。</param>
    /// <param name="Fps">フレームレート。</param>
    public readonly record struct FrameContext(int Frame, int Length, int Fps)
    {
        /// <summary>アイテム内での相対位置を表す <see cref="FrameContext"/> を作ります。</summary>
        public static FrameContext FromItem(TimelineItemSourceDescription description) => new(
            description.ItemPosition.Frame,
            Math.Max(1, description.ItemDuration.Frame),
            Math.Max(1, description.FPS));

        /// <summary>
        /// タイムライン全体での位置を表す <see cref="FrameContext"/> を作ります。
        /// カメラのように、アイテムではなくシーン全体に属するアニメーションの評価に使います。
        /// </summary>
        /// <remarks>
        /// 呼び出し経路によってはタイムラインの長さが得られないことがあります。
        /// その場合はアイテム内の位置で代用します。長さを 1 として扱ってしまうと
        /// アニメーション全体が 1 フレームに圧縮されて見えるためです。
        /// </remarks>
        public static FrameContext FromTimeline(TimelineItemSourceDescription description)
        {
            if (description.TimelineDuration.Frame <= 0 || description.FPS <= 0)
                return FromItem(description);

            return new FrameContext(
                description.TimelinePosition.Frame,
                description.TimelineDuration.Frame,
                description.FPS);
        }
    }

    /// <summary>
    /// <see cref="Animation"/> を <see cref="FrameContext"/> で評価するための拡張。
    /// </summary>
    public static class AnimationExtensions
    {
        /// <inheritdoc cref="Animation.GetValue(int, int, int)"/>
        public static double GetValue(this Animation animation, in FrameContext context)
            => animation.GetValue(context.Frame, context.Length, context.Fps);

        /// <summary>
        /// 値を <see cref="float"/> で取得します。
        /// 3D の計算はほぼすべて <see cref="float"/> で行うため、キャストを省けます。
        /// </summary>
        public static float GetFloat(this Animation animation, in FrameContext context)
            => (float)animation.GetValue(context.Frame, context.Length, context.Fps);
    }
}
