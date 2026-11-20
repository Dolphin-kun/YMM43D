using System.Numerics;
using YMM43D.Commons;
using YMM43D.Plugin;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public readonly struct GroupLookup
    {
        private const int MaxParentDepth = 16;

        private readonly Placed[]? groups;
        private readonly IVideoItem[]? alive;
        private readonly int frame;
        private readonly int fps;

        private GroupLookup(Placed[]? groups, IVideoItem[] alive, int frame, int fps)
        {
            this.groups = groups;
            this.alive = alive;
            this.frame = frame;
            this.fps = fps;
        }

        public bool IsEmpty => groups is null || groups.Length == 0;

        public static GroupLookup Build(Timeline? timeline, int frame, int fps)
        {
            if (timeline?.Items is not { } items)
                return default;

            List<Placed>? found = null;
            var alive = new List<IVideoItem>();

            foreach (var item in items)
            {
                if (item is IVideoItem video && FrameContext.IsAlive(video, frame))
                    alive.Add(video);

                if (item is not GroupItem group || !Applies(timeline, group, frame))
                    continue;

                var groupTime = FrameContext.ForItem(group, frame, fps);

                (found ??= []).Add(new Placed(group, ItemPlacement.GetWorldMatrix(group, groupTime)));
            }

            return new GroupLookup(found?.ToArray(), [.. alive], frame, fps);
        }

        public Matrix4x4 GetTransform(IItem item) => GetTransform(item, 0);

        private Matrix4x4 GetTransform(IItem item, int depth)
        {
            if (depth < MaxParentDepth && ParentOf(item) is { } parent)
                return BoneOf(item, parent) * ParentMatrix(parent, depth + 1);

            var transform = Matrix4x4.Identity;

            foreach (var group in Parents(item))
                transform *= groups![group].World;

            return transform;
        }

        private Matrix4x4 ParentMatrix(IVideoItem parent, int depth)
        {
            var time = FrameContext.ForItem(parent, frame, fps);
            var turn = parent is ShapeItem { ShapeParameter: ShapeParameter3DBase shape } ? shape.GetTurn(time) : Matrix4x4.Identity;

            return turn * ItemPlacement.GetWorldMatrix(parent, time) * GetTransform(parent, depth);
        }

        private Matrix4x4 BoneOf(IItem child, IVideoItem parent)
        {
            if (child is not ShapeItem { ShapeParameter: ShapeParameter3DBase { ParentBone.Length: > 0 } shape }
                || parent is not ShapeItem { ShapeParameter: Shape3DParameter owner })
            {
                return Matrix4x4.Identity;
            }

            return owner.BoneShift(shape.ParentBone, FrameContext.ForItem(parent, frame, fps)) ?? Matrix4x4.Identity;
        }

        private IVideoItem? ParentOf(IItem item)
        {
            if (alive is null || item is not ShapeItem { ShapeParameter: ShapeParameter3DBase { HasParent: true } shape })
                return null;

            IVideoItem? found = null;

            foreach (var other in alive)
            {
                if (ReferenceEquals(other, item) || other is not ShapeItem { ShapeParameter: ShapeParameter3DBase candidate } || candidate.ItemId != shape.ParentId)
                    continue;

                if (found is null || Math.Abs(other.Layer - item.Layer) < Math.Abs(found.Layer - item.Layer))
                    found = other;
            }

            return found;
        }

        public IReadOnlyList<GroupItem> GetGroups(IItem item)
        {
            var found = new List<GroupItem>();

            foreach (var parent in Parents(item))
                found.Add(groups![parent].Group);

            return found;
        }

        private IEnumerable<int> Parents(IItem item)
        {
            if (IsEmpty)
                yield break;

            var current = item;

            for (var depth = 0; depth < groups!.Length; depth++)
            {
                var parent = FindParent(current);

                if (parent < 0)
                    yield break;

                yield return parent;
                current = groups[parent].Group;
            }
        }

        private int FindParent(IItem item)
        {
            var found = -1;

            for (var i = 0; i < groups!.Length; i++)
            {
                var group = groups[i].Group;

                if (ReferenceEquals(group, item))
                    continue;

                if (item.Layer <= group.Layer || item.Layer > group.Layer + group.GroupRange)
                    continue;

                if (group.IsGroupOnly && group.Group != item.Group)
                    continue;

                if (found < 0 || group.Layer > groups[found].Group.Layer)
                    found = i;
            }

            return found;
        }

        private static bool Applies(Timeline timeline, GroupItem group, int frame)
        {
            if (!LayerVisibility.IsShown(timeline, group) || group.GroupRange <= 0)
                return false;

            if (group.IsComposite)
                return false;

            return FrameContext.IsAlive(group, frame);
        }

        private readonly record struct Placed(GroupItem Group, Matrix4x4 World);
    }
}
