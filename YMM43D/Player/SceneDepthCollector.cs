using System.Numerics;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
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

            // 持ち主を囲むグループ制御の配置。3D では親として掛け、YMM4 が後から 2D で掛ける分は打ち消す。
            public Matrix4x4 OwnerGroupTransform { get; init; } = Matrix4x4.Identity;

            public ScreenPlacement OwnerGroupScreen { get; init; } = ScreenPlacement.None;
        }

        public static SceneView Collect(
            TimelineItemSourceDescription description,
            I3DProvider? self,
            IGraphicsDevicesAndContext? devices = null)
        {
            if (self is null || GroupEffectProbe.IsEvaluating)
                return SceneView.None;

            if (TimelineLookup.Find(description) is not { } timeline || timeline.Items is not { } items)
                return SceneView.None;

            var frame = description.TimelinePosition.Frame;
            var fps = description.FPS;

            var groups = GroupLookup.Build(timeline, frame, fps);
            var flattening = new GroupFlattening(groups, devices, description, frame);

            if (FindOwner(timeline, description.Layer, frame) is not { } owner)
                return SceneView.None;

            var ownerTime = FrameContext.ForItem(owner, frame, fps);

            var casters = new List<Occluder>();
            var found = new List<I3DProvider>();

            foreach (var item in items)
            {
                if (item is not IVideoItem video
                    || !LayerVisibility.IsShown(timeline, video)
                    || !FrameContext.IsAlive(video, frame)
                    || flattening.Flattens(video)
                    || Composes(owner, video)
                    || Composes(video, owner))
                {
                    continue;
                }

                var itemTime = FrameContext.ForItem(video, frame, fps);
                var groupTransform = groups.GetTransform(video);
                var placement = ItemPlacement.GetWorldMatrix(video, itemTime) * groupTransform;

                found.Clear();
                FindProviders(video, found, groups, devices);

                foreach (var provider in found)
                {
                    var world = provider is I3DPlacedInstance placed && placed.TryGetPlacement(out var own)
                        ? GetLocalMatrix(provider) * own * groupTransform
                        : GetLocalMatrix(provider) * placement;

                    casters.Add(new Occluder(provider, world, itemTime));
                }
            }

            var ownerPlacement = ItemPlacement.GetWorldMatrix(owner, ownerTime);
            var ownerScreen = ItemPlacement.GetScreenPlacement(owner, ownerTime);

            var flattened = flattening.Flattens(owner);
            var ownerGroupTransform = flattened ? Matrix4x4.Identity : groups.GetTransform(owner);
            var groupScreen = flattened ? ScreenPlacement.None : GetGroupScreen(groups, owner, frame, fps);

            if (flattened || !IsPlacedIn3D(owner, self) || IsOverriddenByGroup(owner, self, groups, devices))
            {
                return new SceneView(owner, ownerTime, ownerPlacement, ownerScreen, [], casters)
                {
                    OwnerGroupTransform = ownerGroupTransform,
                    OwnerGroupScreen = groupScreen,
                };
            }

            var occluders = new List<Occluder>(casters.Count);

            found.Clear();
            FindProviders(owner, found, groups, devices);

            foreach (var caster in casters)
            {
                if (!found.Contains(caster.Provider))
                    occluders.Add(caster);
            }

            return new SceneView(owner, ownerTime, ownerPlacement, ownerScreen, occluders, casters)
            {
                OwnerGroupTransform = ownerGroupTransform,
                OwnerGroupScreen = groupScreen,
            };
        }

        private static ScreenPlacement GetGroupScreen(in GroupLookup groups, IVideoItem owner, int frame, int fps)
        {
            var screen = ScreenPlacement.None;

            foreach (var group in groups.GetGroups(owner))
                screen = screen.Then(ItemPlacement.GetScreenPlacement(group, FrameContext.ForItem(group, frame, fps)));

            return screen;
        }

        public static IVideoItem? FindOwner(TimelineItemSourceDescription description)
            => TimelineLookup.Find(description) is { } timeline
                ? FindOwner(timeline, description.Layer, description.TimelinePosition.Frame)
                : null;

        private static IVideoItem? FindOwner(Timeline timeline, int layer, int frame)
        {
            foreach (var item in timeline.Items ?? [])
            {
                if (item is IVideoItem video
                    && video.Layer == layer
                    && LayerVisibility.IsShown(timeline, video)
                    && FrameContext.IsAlive(video, frame))
                {
                    return video;
                }
            }

            return null;
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

        public static IEnumerable<I3DProvider> FindSources(IVideoItem item, IGraphicsDevicesAndContext? devices = null)
        {
            var sources = new List<I3DProvider>();

            AddSources(item, sources, devices);

            return sources;
        }

        private static void AddSources(IVideoItem item, List<I3DProvider> into, IGraphicsDevicesAndContext? devices)
        {
            if (item is I3DProvider itemProvider && !into.Contains(itemProvider))
                into.Add(itemProvider);

            if (item is ShapeItem shape
                && Provider3DRegistry.Find(shape.ShapeParameter, devices) is { } shapeProvider
                && !into.Contains(shapeProvider))
            {
                into.Add(shapeProvider);
            }
        }

        public static IReadOnlyList<I3DProvider> FindGroupSolids(
            IVideoItem item, in GroupLookup groups, IGraphicsDevicesAndContext? devices = null)
        {
            var enclosing = groups.GetGroups(item);

            for (var i = enclosing.Count - 1; i >= 0; i--)
            {
                if (LastSolidEffect(enclosing[i]) is { } effect)
                    return effect.GetInstancesAt(item.Layer, devices);
            }

            return [];
        }

        private static VideoEffect3DBase? LastSolidEffect(IVideoItem item)
        {
            VideoEffect3DBase? found = null;

            foreach (var effect in item.VideoEffects ?? [])
            {
                if (effect.IsEnabled && effect is VideoEffect3DBase solid)
                    found = solid;
            }

            return found;
        }

        private static bool IsOverriddenByGroup(
            IVideoItem owner, I3DProvider self, in GroupLookup groups, IGraphicsDevicesAndContext? devices)
            => (owner.VideoEffects ?? []).Any(effect => ReferenceEquals(effect, self))
            && FindGroupSolids(owner, groups, devices).Count > 0;

        private static void FindProviders(
            IVideoItem item, List<I3DProvider> into, in GroupLookup groups, IGraphicsDevicesAndContext? devices)
        {
            if (item is GroupItem { IsComposite: false })
                return;

            if (FindGroupSolids(item, groups, devices) is { Count: > 0 } solids)
            {
                into.AddRange(solids);
                return;
            }

            var effects = item.VideoEffects;

            if (effects is null)
            {
                AddSources(item, into, devices);
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
                AddSources(item, into, devices);
                return;
            }

            if (last >= 0 && effects[last] is I3DProvider placed)
            {
                foreach (var instance in Instances(placed, devices))
                {
                    if (!into.Contains(instance))
                        into.Add(instance);
                }
            }
        }

        public static IReadOnlyList<I3DProvider> Instances(I3DProvider provider, IGraphicsDevicesAndContext? devices = null)
            => provider switch
            {
                VideoEffect3DBase effect => effect.GetInstances(devices),
                I3DInstances instances => instances.GetInstances(),
                _ => [provider],
            };

        public static bool Composes(IVideoItem composer, IVideoItem composed)
        {
            if (ReferenceEquals(composer, composed))
                return false;

            return composer switch
            {
                GroupItem { IsComposite: true } group =>
                    composed.Layer > group.Layer
                    && composed.Layer <= group.Layer + group.GroupRange
                    && (!group.IsGroupOnly || group.Group == composed.Group),
                FrameBufferItem or EffectItem => composed.Layer < composer.Layer,
                _ => false,
            };
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
