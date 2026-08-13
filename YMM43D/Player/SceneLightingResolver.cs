using YMM43D.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class SceneLightingResolver
    {
        public static SceneLighting Resolve(TimelineItemSourceDescription description)
            => Resolve(
                TimelineLookup.Find(description),
                description.TimelinePosition.Frame,
                description.FPS);

        public static SceneLighting Resolve(Timeline timeline)
            => Resolve(timeline, timeline.CurrentFrame, Math.Max(1, timeline.VideoInfo.FPS));

        public static SceneLighting Resolve(Timeline? timeline, int frame, int fps)
        {
            if (timeline?.Items is not { } items)
                return SceneLighting.Default;

            var lights = new List<SceneLight>();

            IItem? environmentItem = null;
            ISceneEnvironment? environment = null;

            foreach (var item in items)
            {
                if (item.IsHidden || frame < item.Frame || frame >= item.Frame + item.Length)
                    continue;

                var itemTime = new FrameContext(frame - item.Frame, Math.Max(1, item.Length), fps);

                if (item is ISceneLightSource source && lights.Count < SceneLighting.MaxLights)
                    lights.Add(source.GetLight(itemTime));

                if (item is not ISceneEnvironment candidate)
                    continue;

                if (environmentItem is { } current && item.Layer <= current.Layer)
                    continue;

                environmentItem = item;
                environment = candidate;
            }

            if (lights.Count == 0)
                lights = [.. SceneLighting.Default.Lights];

            if (environment is null || environmentItem is null)
                return new SceneLighting(lights, SceneLighting.Default.Ambient, SceneFog.None);

            var time = new FrameContext(
                frame - environmentItem.Frame, Math.Max(1, environmentItem.Length), fps);

            return new SceneLighting(lights, environment.GetAmbient(time), environment.GetFog(time));
        }
    }
}
