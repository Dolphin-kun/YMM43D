using System.Numerics;
using YMM43D.Commons;
using YMM43D.Player;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Project.Shape.Modifiers
{
    internal static class BooleanTargets
    {
        public static Func<int, BooleanTarget?>? Resolve(TimelineItemSourceDescription description, Shape3DParameter self)
        {
            if (TimelineLookup.Find(description) is not { Items: { } items } timeline || SceneDepthCollector.FindOwner(description) is not { } owner)
                return null;

            var frame = description.TimelinePosition.Frame;
            var fps = description.FPS;
            var groups = GroupLookup.Build(timeline, frame, fps);
            var ownerTime = FrameContext.ForItem(owner, frame, fps);
            var selfWorld = self.GetLocalMatrix(ownerTime) * ItemPlacement.GetWorldMatrix(owner, ownerTime) * groups.GetTransform(owner);

            if (!Matrix4x4.Invert(selfWorld, out var back))
                return null;

            var found = new Dictionary<int, BooleanTarget?>();

            return layer =>
            {
                if (found.TryGetValue(layer, out var known))
                    return known;

                BooleanTarget? target = null;

                if (items.OfType<IVideoItem>().FirstOrDefault(item => item.Layer == layer && FrameContext.IsAlive(item, frame)) is ShapeItem { ShapeParameter: Shape3DParameter other } item
                    && !ReferenceEquals(other, self))
                {
                    var time = FrameContext.ForItem(item, frame, fps);
                    var world = other.GetLocalMatrix(time) * ItemPlacement.GetWorldMatrix(item, time) * groups.GetTransform(item);

                    target = new BooleanTarget(other.BareMesh(time), world * back);
                }

                found[layer] = target;

                return target;
            };
        }
    }
}
