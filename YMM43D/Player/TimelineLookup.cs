using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class TimelineLookup
    {
        private static readonly Lock Gate = new();
        private static readonly List<WeakReference<Timeline>> Seen = [];

        public static Timeline? Find(TimelineSourceDescription description)
        {
            Timeline? found = null;

            foreach (var info in description.Scenes ?? [])
            {
                if (info is not Scene scene)
                    continue;

                Remember(scene.Timeline);

                if (scene.ID == description.SceneId)
                    found = scene.Timeline;
            }

            return found;
        }

        public static Timeline? Holding(IItem item)
        {
            lock (Gate)
            {
                foreach (var seen in Seen)
                {
                    if (seen.TryGetTarget(out var timeline) && timeline.Items.Contains(item))
                        return timeline;
                }
            }

            return null;
        }

        private static void Remember(Timeline timeline)
        {
            lock (Gate)
            {
                Seen.RemoveAll(seen => !seen.TryGetTarget(out _));

                if (!Seen.Any(seen => seen.TryGetTarget(out var known) && ReferenceEquals(known, timeline)))
                    Seen.Add(new WeakReference<Timeline>(timeline));
            }
        }
    }
}
