using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// <see cref="Animation"/> の値を取り出すのに必要な時間情報の組。
    /// </summary>
    /// <remarks>
    /// YMM4 の <c>Animation.GetValue(frame, length, fps)</c> は3つの引数を常にセットで
    /// 要求するため、1つの型にまとめてあります。
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
        /// タイムラインの長さが得られない経路ではアイテム内の位置で代用します。
        /// 長さを 1 にするとアニメーション全体が 1 フレームに圧縮されて見えるためです。
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

        /// <summary>
        /// キーフレームを壊さずに、すべての値を同じだけ動かします。
        /// </summary>
        /// <remarks>
        /// プレビュー上のドラッグをアニメーションに反映するのに使います。その瞬間の値を
        /// 書き込むと打ってある動きが消えるので、差分を全部の値に足します。
        /// <para>
        /// <see cref="Animation.AddToEachValues"/> は上下限で丸めません。範囲の外へ出た分が
        /// そのまま溜まり、逆へドラッグしたときに同じ量だけ空回りします。足せるぶんだけに
        /// 削ってから渡します。
        /// </para>
        /// </remarks>
        public static void Nudge(this Animation animation, double delta)
        {
            if (delta == 0 || animation.Values is not { Count: > 0 } values)
                return;

            var room = Math.Min(animation.MaxValue - values.Max(v => v.Value), delta);
            room = Math.Max(animation.MinValue - values.Min(v => v.Value), room);

            // すでに範囲外にある場合、削った結果が向きごと反転することがある。
            if (room == 0 || Math.Sign(room) != Math.Sign(delta))
                return;

            animation.AddToEachValues(room);
        }

        /// <summary>
        /// その位置に中間点を打って、そこの値だけを動かします。
        /// </summary>
        /// <param name="frame">アイテムの先頭から数えたフレーム位置。</param>
        /// <remarks>
        /// YMM4 のキーフレームはアイテムに1組しかなく、値の並びは「先頭・各中間点・末尾」の
        /// 順です。中間点が <c>n</c> 個あれば値は <c>n+2</c> 個で、<c>i</c> 番目の中間点は
        /// <c>i+1</c> 番目の値に対応します。
        /// <para>
        /// 移動方法が「なし」のままでは値を1つしか持てないので、中間点を打つ前に
        /// 「直線移動」へ変えます。値が1つのうちは変えても見え方は変わりません。
        /// </para>
        /// <para>
        /// 打てない場面（先頭・末尾など）では <see cref="Nudge"/> と同じ動きに戻します。
        /// 打てないからといって何も動かないと、ドラッグが効かなくなってしまいます。
        /// </para>
        /// </remarks>
        public static void NudgeAt(this Animation animation, double delta, int frame)
        {
            if (delta == 0)
                return;

            if (frame <= 0 || animation.KeyFrames is not { } keyFrames)
            {
                // 先頭の値は中間点ではなく、値の並びの 0 番目そのもの。
                NudgeValue(animation, delta, 0);
                return;
            }

            if (!AnimationTypeEx.IsKeyFrameSupported(animation.AnimationType))
                animation.AnimationType = AnimationType.直線移動;

            var index = keyFrames.Frames.IndexOf(frame);
            if (index < 0)
                index = keyFrames.Insert(frame);

            NudgeValue(animation, delta, index + 1);
        }

        /// <summary>値の並びの1つだけを、上下限に収めて動かします。</summary>
        private static void NudgeValue(Animation animation, double delta, int index)
        {
            if (animation.Values is not { } values || index < 0 || index >= values.Count)
            {
                // 値が揃っていない。打った中間点は残るが、動かす先が無い。
                animation.Nudge(delta);
                return;
            }

            var target = values[index];

            target.Value = Math.Clamp(target.Value + delta, animation.MinValue, animation.MaxValue);
        }
    }
}
