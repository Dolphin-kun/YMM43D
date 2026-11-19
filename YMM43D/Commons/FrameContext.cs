using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Commons
{
    public readonly record struct FrameContext(int Frame, int Length, int Fps)
    {
        public static FrameContext FromItem(TimelineItemSourceDescription description)
            => new(description.ItemPosition.Frame, Math.Max(1, description.ItemDuration.Frame), Math.Max(1, description.FPS));

        public static FrameContext ForItem(IItem item, int timelineFrame, int fps) => new(timelineFrame - item.Frame, Math.Max(1, item.Length), fps);

        public static bool IsAlive(IItem item, int timelineFrame)
            => timelineFrame >= item.Frame && timelineFrame < item.Frame + item.Length;
    }
}
