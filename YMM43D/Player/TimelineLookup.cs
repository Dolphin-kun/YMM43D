using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;

namespace YMM43D.Player
{
    public static class TimelineLookup
    {
        public static Timeline? Find(TimelineSourceDescription description)
        {
            foreach (var info in description.Scenes ?? [])
            {
                if (info is Scene scene && scene.ID == description.SceneId)
                    return scene.Timeline;
            }

            return null;
        }
    }
}
