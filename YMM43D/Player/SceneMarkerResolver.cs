using YMM43D.Commons;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class SceneMarkerResolver
    {
        public readonly record struct PlacedMarker(
            IItem Item,
            ISceneMarkerSource Source,
            FrameContext ItemTime,
            SceneMarker Marker);

        public static IReadOnlyList<PlacedMarker> Resolve(Timeline? timeline)
        {
            if (timeline?.Items is not { } items)
                return [];

            var frame = timeline.CurrentFrame;
            var fps = Math.Max(1, timeline.VideoInfo.FPS);
            var found = new List<PlacedMarker>();

            foreach (var item in items)
            {
                if (item is not ISceneMarkerSource source || !LayerVisibility.IsShown(timeline, item))
                    continue;

                if (frame < item.Frame || frame >= item.Frame + item.Length)
                    continue;

                var itemTime = new FrameContext(frame - item.Frame, Math.Max(1, item.Length), fps);

                found.Add(new PlacedMarker(item, source, itemTime, source.GetMarker(itemTime)));
            }

            return found;
        }
    }
}
