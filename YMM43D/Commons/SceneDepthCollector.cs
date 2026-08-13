using System.Numerics;
using YMM43D.Scene3D;
using YMM43D.Plugin;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Commons
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
                .Where(item => !item.IsHidden && ItemPlacement.IsAliveAt(item, frame))
                .Select(item => (Item: item, Time: new FrameContext(frame - item.Frame, item.Length, fps)))
                .ToArray();

            var (owner, ownerTime) = alive.FirstOrDefault(x => x.Item.Layer == description.Layer);
            if (owner is null)
                return SceneView.None;

            var ownerPlacement = ItemPlacement.GetWorldMatrix(owner, ownerTime, Matrix4x4.Identity);
            var ownerScreen = ItemPlacement.GetScreenPlacement(owner, ownerTime);

            var occluders = new List<Occluder>();

            foreach (var (item, itemTime) in alive)
            {
                if (ReferenceEquals(item, owner))
                    continue;

                var placement = ItemPlacement.GetWorldMatrix(item, itemTime, Matrix4x4.Identity);

                foreach (var provider in FindProviders(item))
                    occluders.Add(new Occluder(provider, GetLocalMatrix(provider) * placement, itemTime));
            }

            return new SceneView(owner, ownerTime, ownerPlacement, ownerScreen, occluders);
        }

        private static IEnumerable<I3DProvider> FindProviders(IVideoItem item)
        {
            var providers = new List<I3DProvider>();

            if (item is I3DProvider itemProvider)
                providers.Add(itemProvider);

            if (item is ShapeItem shape && Provider3DRegistry.Find(shape.ShapeParameter) is { } shapeProvider)
                providers.Add(shapeProvider);

            if (providers.Count > 0)
                return providers.Distinct();

            foreach (var effect in item.VideoEffects ?? [])
            {
                if (effect.IsEnabled && effect is I3DProvider effectProvider)
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
