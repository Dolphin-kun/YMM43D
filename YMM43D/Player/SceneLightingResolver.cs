using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class SceneLightingResolver
    {
        public static SceneLighting Resolve(TimelineItemSourceDescription description)
            => Resolve(TimelineLookup.Find(description), description.TimelinePosition.Frame, description.FPS);

        public static SceneLighting Resolve(Timeline timeline) => Resolve(timeline, timeline.CurrentFrame, Math.Max(1, timeline.VideoInfo.FPS));

        public static SceneLighting Resolve(Timeline? timeline, int frame, int fps)
        {
            if (timeline?.Items is not { } items)
                return SceneLighting.Default;

            var lights = new List<SceneLight>();
            var flashes = new List<SceneLight>();
            var groups = GroupLookup.Build(timeline, frame, fps);

            IItem? environmentItem = null;

            foreach (var item in items)
            {
                if (!LayerVisibility.IsShown(timeline, item) || !FrameContext.IsAlive(item, frame))
                    continue;

                var itemTime = FrameContext.ForItem(item, frame, fps);

                if (item is ISceneLightSource source)
                    lights.Add(source.GetLight(itemTime));

                if (item is IVideoItem video)
                    AddPlacedLights(video, itemTime, groups, lights, flashes);

                if (item is not ISceneEnvironment)
                    continue;

                if (environmentItem is { } current && item.Layer <= current.Layer)
                    continue;

                environmentItem = item;
            }

            if (lights.Count == 0)
                lights = [.. SceneLighting.Default.Lights];

            lights.AddRange(flashes);

            if (environmentItem is not ISceneEnvironment environment)
                return new SceneLighting(lights, SceneLighting.Default.Ambient, SceneFog.None);

            var time = FrameContext.ForItem(environmentItem, frame, fps);

            return new SceneLighting(
                lights,
                environment.GetAmbient(time),
                environment.GetFog(time),
                environment.ShadowResolution,
                environment.GetGround(time),
                environment.GetMirror(time));
        }

        private static void AddPlacedLights(IVideoItem video, in FrameContext itemTime, in GroupLookup groups, List<SceneLight> lights, List<SceneLight> flashes)
        {
            var found = FindPlacedLight(video) is { IsLightEnabled: true } placed ? placed : null;
            var effects = video.VideoEffects ?? [];
            var lit = effects.Any(IsLitEffect);

            if (found is null && !lit)
                return;

            var placement = ItemPlacement.GetWorldMatrix(video, itemTime) * groups.GetTransform(video);

            if (found is not null)
                lights.Add(found.GetLight(itemTime, placement));

            if (!lit)
                return;

            foreach (var effect in effects)
            {
                if (!IsLitEffect(effect) || effect is not IPlacedSceneLightSource source)
                    continue;

                var light = source.GetLight(itemTime, placement);

                if (light.Color != Vector3.Zero)
                    flashes.Add(light);
            }
        }

        private static bool IsLitEffect(IVideoEffect effect) => effect.IsEnabled && effect is IPlacedSceneLightSource { IsLightEnabled: true };

        private static IPlacedSceneLightSource? FindPlacedLight(IItem item) => item switch
        {
            IPlacedSceneLightSource placed => placed,
            ShapeItem shape => shape.ShapeParameter as IPlacedSceneLightSource,
            _ => null,
        };
    }
}
