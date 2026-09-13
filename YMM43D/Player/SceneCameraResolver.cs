
using YMM43D.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class SceneCameraResolver
    {
        public readonly record struct ActiveCamera(IItem Item, ISceneCamera Source, FrameContext ItemTime);

        public static CameraState Resolve(TimelineItemSourceDescription description)
            => Resolve(TimelineLookup.Find(description), description.TimelinePosition.Frame, description.FPS);

        public static CameraState Resolve(Timeline timeline)
            => Resolve(timeline, timeline.CurrentFrame, Math.Max(1, timeline.VideoInfo.FPS));

        private static CameraState Resolve(Timeline? timeline, int frame, int fps)
            => Find(timeline, frame, fps) is { } active
                ? active.Source.GetState(active.ItemTime)
                : CameraState.Default;

        public static ActiveCamera? Find(Timeline? timeline, int frame, int fps)
        {
            if (timeline?.Items is not { } items)
                return null;

            ActiveCamera? found = null;

            foreach (var candidate in items)
            {
                if (candidate is not ISceneCamera camera || !LayerVisibility.IsShown(timeline, candidate))
                    continue;

                if (!FrameContext.IsAlive(candidate, frame))
                    continue;

                if (found is { } current && candidate.Layer <= current.Item.Layer)
                    continue;

                found = new ActiveCamera(candidate, camera, FrameContext.ForItem(candidate, frame, fps));
            }

            return found;
        }

        public static ActiveCamera? Find(Timeline timeline)
            => Find(timeline, timeline.CurrentFrame, Math.Max(1, timeline.VideoInfo.FPS));
    }
}
