using YukkuriMovieMaker.Commons;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// プレビュー上の操作を、アニメーションのどこに書き込むか。
    /// </summary>
    /// <remarks>
    /// 既定では、打ってあるキーフレームすべてを同じだけずらします。動きを壊さずに
    /// 全体を置き直せますが、これだけでは動きそのものを作れません。
    /// <see cref="AtFrame"/> を使うと、その瞬間に中間点を打ってそこだけを動かします。
    /// </remarks>
    /// <param name="InsertsKeyFrame">中間点を打って、その位置だけを動かすかどうか。</param>
    /// <param name="Frame">アイテムの先頭から数えたフレーム位置。</param>
    public readonly record struct EditScope(bool InsertsKeyFrame, int Frame)
    {
        /// <summary>打ってあるキーフレームを全部ずらします。</summary>
        public static EditScope Whole => default;

        /// <summary>その位置に中間点を打って、そこだけを動かします。</summary>
        public static EditScope AtFrame(int frame) => new(true, Math.Max(0, frame));

        /// <summary>
        /// 動かす量をアニメーションに反映します。
        /// </summary>
        public void Nudge(Animation animation, double delta)
        {
            if (InsertsKeyFrame)
                animation.NudgeAt(delta, Frame);
            else
                animation.Nudge(delta);
        }
    }
}
