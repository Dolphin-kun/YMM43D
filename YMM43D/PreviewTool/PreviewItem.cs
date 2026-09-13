using System.Collections.Immutable;
using YMM43D.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class PreviewItem(
        I3DProvider provider,
        ImmutableList<IVideoEffect> effects,
        IVideoItem item)
    {
        public I3DProvider Provider { get; } = provider;

        public ImmutableList<IVideoEffect> Effects { get; } = effects;

        public IVideoItem Item { get; } = item;

        public FrameContext GetItemTime(in FrameContext timelineTime)
            => FrameContext.ForItem(Item, timelineTime.Frame, timelineTime.Fps);
    }
}
