using System.Collections.Concurrent;
using YMM43D.Commons;

using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public sealed class TimelineRefresher
    {
        private const long MinIntervalMs = 120;

        private static readonly ConcurrentDictionary<Guid, TimelineRefresher> shared = new();

        private readonly CameraChangeTracker tracker = new();

        private long lastRefreshAt;
        private int lastKnownFrame = -1;
        private bool isPending;

        public static TimelineRefresher For(Timeline timeline) => shared.GetOrAdd(timeline.ID, _ => new TimelineRefresher());

        public bool RefreshIfCameraChanged(Timeline timeline)
        {
            if (tracker.HasChanged(SceneCameraResolver.Resolve(timeline), Resolve(timeline)))
                isPending = true;

            return Apply(timeline);
        }

        public void ForceRefresh(Timeline timeline)
        {
            isPending = true;

            if (!IsDue)
                return;

            tracker.Sync(SceneCameraResolver.Resolve(timeline), Resolve(timeline));

            Apply(timeline);
        }

        private static SceneLighting Resolve(Timeline timeline)
            => SceneLightingResolver.Resolve(timeline);

        private bool IsDue => Environment.TickCount64 - lastRefreshAt >= MinIntervalMs;

        private bool Apply(Timeline timeline)
        {
            if (!isPending || !IsDue)
                return false;

            lastRefreshAt = Environment.TickCount64;
            isPending = false;

            var frame = timeline.CurrentFrame;
            var isAdvancing = lastKnownFrame >= 0 && lastKnownFrame != frame;
            lastKnownFrame = frame;

            NotifyCameraSync(timeline);

            if (!isAdvancing)
            {
                NudgeCurrentFrame(timeline);
                lastKnownFrame = timeline.CurrentFrame;
            }

            return true;
        }

        public static FrameContext GetTime(Timeline timeline)
            => new(timeline.CurrentFrame, Math.Max(1, timeline.Length), Math.Max(1, timeline.VideoInfo.FPS));

        private static void NotifyCameraSync(Timeline timeline)
        {
            if (timeline.Items is null)
                return;

            foreach (var item in timeline.Items)
            {
                if (item is ShapeItem shape && shape.ShapeParameter is ICameraSync shapeSync)
                    shapeSync.TouchCameraSync();

                if (item is not IVideoItem videoItem || videoItem.VideoEffects is null)
                    continue;

                foreach (var effect in videoItem.VideoEffects)
                {
                    if (effect is ICameraSync effectSync)
                        effectSync.TouchCameraSync();
                }
            }
        }

        private static void NudgeCurrentFrame(Timeline timeline)
        {
            var current = timeline.CurrentFrame;
            var neighbour = current < timeline.Length - 1
                ? current + 1
                : current - 1;

            if (neighbour < 0)
                return;

            timeline.CurrentFrame = neighbour;
            timeline.CurrentFrame = current;
        }
    }
}
