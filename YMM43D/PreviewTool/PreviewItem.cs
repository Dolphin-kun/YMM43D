using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class PreviewItem(I3DProvider provider, IVideoItem item, int startFrame, int length)
    {
        public I3DProvider Provider { get; } = provider;
        public IVideoItem Item { get; } = item;

        public int StartFrame { get; } = startFrame;

        public int Length { get; } = length;

        public FrameContext GetItemTime(in FrameContext timelineTime)
            => new(timelineTime.Frame - StartFrame, Math.Max(1, Length), timelineTime.Fps);
    }
}
