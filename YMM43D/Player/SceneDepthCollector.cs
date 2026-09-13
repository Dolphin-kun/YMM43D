using System.Numerics;
using YMM43D.Commons;

using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class SceneDepthCollector
    {
        public readonly record struct Occluder(I3DProvider Provider, Matrix4x4 World, FrameContext Time);

        public readonly record struct SceneView(
            IVideoItem? Owner,
            FrameContext OwnerTime,
            Matrix4x4 OwnerPlacement,
            ScreenPlacement OwnerScreenPlacement,
            IReadOnlyList<Occluder> Occluders,
            IReadOnlyList<Occluder> Casters)
        {
            public static SceneView None
                => new(null, default, Matrix4x4.Identity, ScreenPlacement.None, [], []);
        }

        public static SceneView Collect(
            TimelineItemSourceDescription description,
            I3DProvider? self)
        {
            if (self is null)
                return SceneView.None;

            if (TimelineLookup.Find(description) is not { } timeline || timeline.Items is not { } items)
                return SceneView.None;

            var frame = description.TimelinePosition.Frame;
            var fps = description.FPS;

            var groups = GroupLookup.Build(timeline, frame, fps);

            IVideoItem? owner = null;
            var ownerTime = default(FrameContext);

            var casters = new List<Occluder>();
            var found = new List<I3DProvider>();

            foreach (var item in items)
            {
                if (item is not IVideoItem video
                    || !LayerVisibility.IsShown(timeline, video)
                    || !FrameContext.IsAlive(video, frame))
                {
                    continue;
                }

                var itemTime = FrameContext.ForItem(video, frame, fps);

                if (owner is null && video.Layer == description.Layer)
                {
                    owner = video;
                    ownerTime = itemTime;
                }

                var placement = ItemPlacement.GetWorldMatrix(video, itemTime) * groups.GetTransform(video);

                found.Clear();
                FindProviders(video, found);

                foreach (var provider in found)
                    casters.Add(new Occluder(provider, GetLocalMatrix(provider) * placement, itemTime));
            }

            if (owner is null)
                return SceneView.None;

            var ownerPlacement = ItemPlacement.GetWorldMatrix(owner, ownerTime);
            var ownerScreen = ItemPlacement.GetScreenPlacement(owner, ownerTime);

            if (!IsPlacedIn3D(owner, self))
                return new SceneView(owner, ownerTime, ownerPlacement, ownerScreen, [], casters);

            var occluders = new List<Occluder>(casters.Count);

            found.Clear();
            FindProviders(owner, found);

            foreach (var caster in casters)
            {
                if (!found.Contains(caster.Provider))
                    occluders.Add(caster);
            }

            return new SceneView(owner, ownerTime, ownerPlacement, ownerScreen, occluders, casters);
        }

        public static bool IsPlacedIn3D(IVideoItem item, I3DProvider provider)
        {
            var effects = item.VideoEffects;

            if (effects is null)
                return true;

            var at = -1;
            var last = -1;

            for (var i = 0; i < effects.Count; i++)
            {
                if (effects[i].IsEnabled)
                    last = i;

                if (ReferenceEquals(effects[i], provider))
                    at = i;
            }

            return at < 0 || at == last;
        }

        public static bool HasSolidEffect(IVideoItem item)
        {
            foreach (var effect in item.VideoEffects ?? [])
            {
                if (effect.IsEnabled && effect is I3DProvider)
                    return true;
            }

            return false;
        }

        public static IEnumerable<I3DProvider> FindSources(IVideoItem item)
        {
            var sources = new List<I3DProvider>();

            AddSources(item, sources);

            return sources;
        }

        private static void AddSources(IVideoItem item, List<I3DProvider> into)
        {
            if (item is I3DProvider itemProvider && !into.Contains(itemProvider))
                into.Add(itemProvider);

            if (item is ShapeItem shape
                && Provider3DRegistry.Find(shape.ShapeParameter) is { } shapeProvider
                && !into.Contains(shapeProvider))
            {
                into.Add(shapeProvider);
            }
        }

        private static void FindProviders(IVideoItem item, List<I3DProvider> into)
        {
            var effects = item.VideoEffects;

            if (effects is null)
            {
                AddSources(item, into);
                return;
            }

            var last = -1;
            var solid = false;

            for (var i = 0; i < effects.Count; i++)
            {
                if (!effects[i].IsEnabled)
                    continue;

                last = i;
                solid |= effects[i] is I3DProvider;
            }

            if (!solid)
            {
                AddSources(item, into);
                return;
            }

            if (last >= 0 && effects[last] is I3DProvider placed && !into.Contains(placed))
                into.Add(placed);
        }

        private static Matrix4x4 GetLocalMatrix(I3DProvider provider)
        {
            if (provider is I3DLocalTransform transform && transform.TryGetLocalMatrix(out var matrix))
                return matrix;

            if (provider is I3DSizeProvider sizeProvider
                && sizeProvider.ScalesToInputSize
                && sizeProvider.TryGetSize(out var size, out var offset))
            {
                return WorldScale.CreateSizeMatrix(size, offset + size / 2f);
            }

            return Matrix4x4.Identity;
        }
    }
}
