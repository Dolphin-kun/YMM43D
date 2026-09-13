using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public readonly struct GroupLookup
    {
        private readonly Placed[]? groups;

        private readonly record struct Placed(GroupItem Group, Matrix4x4 World);

        private GroupLookup(Placed[] groups) => this.groups = groups;

        public bool IsEmpty => groups is null || groups.Length == 0;

        public static GroupLookup Build(Timeline? timeline, int frame, int fps)
        {
            if (timeline?.Items is not { } items)
                return default;

            List<Placed>? found = null;

            foreach (var item in items)
            {
                if (item is not GroupItem group || !Applies(timeline, group, frame))
                    continue;

                var groupTime = FrameContext.ForItem(group, frame, fps);

                (found ??= []).Add(new Placed(group, ItemPlacement.GetWorldMatrix(group, groupTime)));
            }

            return found is null ? default : new GroupLookup([.. found]);
        }

        public Matrix4x4 GetTransform(IItem item)
        {
            if (groups is null || groups.Length == 0)
                return Matrix4x4.Identity;

            var transform = Matrix4x4.Identity;
            var current = item;

            for (var depth = 0; depth < groups.Length; depth++)
            {
                var parent = FindParent(current);

                if (parent < 0)
                    break;

                transform *= groups[parent].World;
                current = groups[parent].Group;
            }

            return transform;
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
    }

    public static class TimelineGroups
    {
        public static Matrix4x4 GetTransform(Timeline? timeline, IItem item, int frame, int fps)
            => GroupLookup.Build(timeline, frame, fps).GetTransform(item);
    }
}
