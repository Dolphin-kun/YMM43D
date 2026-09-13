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
            var groups = GroupLookup.Build(timeline, frame, fps);

            IItem? environmentItem = null;

            foreach (var item in items)
            {
                if (!LayerVisibility.IsShown(timeline, item)
                    || !FrameContext.IsAlive(item, frame))
                {
                    continue;
                }

                var itemTime = FrameContext.ForItem(item, frame, fps);

                if (item is ISceneLightSource source)
                    lights.Add(source.GetLight(itemTime));

                if (FindPlacedLight(item) is { } placed && placed.IsLightEnabled && item is IVideoItem video)
                {
                    lights.Add(placed.GetLight(
                        itemTime,
                        ItemPlacement.GetWorldMatrix(video, itemTime) * groups.GetTransform(item)));
                }

                if (item is not ISceneEnvironment)
                    continue;

                if (environmentItem is { } current && item.Layer <= current.Layer)
                    continue;

                environmentItem = item;
            }

            if (lights.Count == 0)
                lights = [.. SceneLighting.Default.Lights];

            if (environmentItem is not ISceneEnvironment environment)
                return new SceneLighting(lights, SceneLighting.Default.Ambient, SceneFog.None);

            var time = FrameContext.ForItem(environmentItem, frame, fps);

            return new SceneLighting(
                lights, environment.GetAmbient(time), environment.GetFog(time), environment.ShadowResolution);
        }

        private static IPlacedSceneLightSource? FindPlacedLight(IItem item) => item switch
        {
            IPlacedSceneLightSource placed => placed,
            ShapeItem shape => shape.ShapeParameter as IPlacedSceneLightSource,
            _ => null,
        };
    }
}
