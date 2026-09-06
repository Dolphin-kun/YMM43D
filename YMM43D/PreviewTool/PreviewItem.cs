using System.Collections.Immutable;
using YMM43D.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class PreviewItem(
        I3DProvider provider,
        ImmutableList<IVideoEffect> effects,
        IVideoItem item,
        int startFrame,
        int length)
    {
        public I3DProvider Provider { get; } = provider;

        public ImmutableList<IVideoEffect> Effects { get; } = effects;

        public IVideoItem Item { get; } = item;

        public int StartFrame { get; } = startFrame;

        public int Length { get; } = length;

        public FrameContext GetItemTime(in FrameContext timelineTime)
            => new(timelineTime.Frame - StartFrame, Math.Max(1, Length), timelineTime.Fps);
    }
}
