using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// 3Dプレビューに表示する 1 件。アイテムと、それを描画するプロバイダーの組。
    /// </summary>
    /// <remarks>
    /// 1つのアイテムが複数のプロバイダー（アイテム自身と、掛かっているエフェクト）を
    /// 持つことがあるため、アイテムとプロバイダーは 1 対 1 ではありません。
    /// </remarks>
    internal sealed class PreviewItem(I3DProvider provider, IVideoItem item, int startFrame, int length)
    {
        public I3DProvider Provider { get; } = provider;
        public IVideoItem Item { get; } = item;

        /// <summary>タイムライン上でアイテムが始まるフレーム。</summary>
        public int StartFrame { get; } = startFrame;

        /// <summary>アイテムの長さ（フレーム数）。</summary>
        public int Length { get; } = length;

        /// <summary>
        /// タイムライン上の位置を、このアイテム内での相対位置に変換します。
        /// </summary>
        public FrameContext GetItemTime(in FrameContext timelineTime)
            => new(timelineTime.Frame - StartFrame, Math.Max(1, Length), timelineTime.Fps);
    }
}
