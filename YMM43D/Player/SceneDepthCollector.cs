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
            IReadOnlyList<Occluder> Occluders)
        {
            public static SceneView None
                => new(null, default, Matrix4x4.Identity, ScreenPlacement.None, []);
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

            var alive = items
                .OfType<IVideoItem>()
                .Where(item => LayerVisibility.IsShown(timeline, item) && ItemPlacement.IsAliveAt(item, frame))
                .Select(item => (Item: item, Time: new FrameContext(frame - item.Frame, item.Length, fps)))
                .ToArray();

            var (owner, ownerTime) = alive.FirstOrDefault(x => x.Item.Layer == description.Layer);
            if (owner is null)
                return SceneView.None;

            var ownerPlacement = ItemPlacement.GetWorldMatrix(owner, ownerTime);
            var ownerScreen = ItemPlacement.GetScreenPlacement(owner, ownerTime);

            var occluders = new List<Occluder>();

            if (!IsPlacedIn3D(owner, self))
                return new SceneView(owner, ownerTime, ownerPlacement, ownerScreen, occluders);

            foreach (var (item, itemTime) in alive)
            {
                if (ReferenceEquals(item, owner))
                    continue;

                var placement = ItemPlacement.GetWorldMatrix(item, itemTime);

                foreach (var provider in FindProviders(item))
                    occluders.Add(new Occluder(provider, GetLocalMatrix(provider) * placement, itemTime));
            }

            return new SceneView(owner, ownerTime, ownerPlacement, ownerScreen, occluders);
        }

        public static bool IsPlacedIn3D(IVideoItem item, I3DProvider provider)
        {
            var effects = (item.VideoEffects ?? []).ToArray();
            var last = Array.FindLastIndex(effects, effect => effect.IsEnabled);

            for (var i = 0; i < effects.Length; i++)
            {
                if (ReferenceEquals(effects[i], provider))
                    return i == last;
            }

            return true;
        }

        public static bool HasSolidEffect(IVideoItem item)
            => (item.VideoEffects ?? []).Any(effect => effect.IsEnabled && effect is I3DProvider);

        public static IEnumerable<I3DProvider> FindSources(IVideoItem item)
        {
            var sources = new List<I3DProvider>();

            if (item is I3DProvider itemProvider)
                sources.Add(itemProvider);

            if (item is ShapeItem shape && Provider3DRegistry.Find(shape.ShapeParameter) is { } shapeProvider)
                sources.Add(shapeProvider);

            return sources.Distinct();
        }

        private static IEnumerable<I3DProvider> FindProviders(IVideoItem item)
        {
            if (!HasSolidEffect(item))
                return FindSources(item);

            var providers = new List<I3DProvider>();

            foreach (var effect in item.VideoEffects ?? [])
            {
                if (effect.IsEnabled && effect is I3DProvider effectProvider && IsPlacedIn3D(item, effectProvider))
                    providers.Add(effectProvider);
            }

            return providers.Distinct();
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
