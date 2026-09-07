using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class TimelineGroups
    {
        // グループ制御は、自分より下の「レイヤー範囲」に入るアイテムをまとめて動かす。
        // 入れ子にもなるので、いちばん近い親から外側へたどって掛け合わせる。
        public static Matrix4x4 GetTransform(Timeline? timeline, IItem item, int frame, int fps)
        {
            if (timeline?.Items is not { } items)
                return Matrix4x4.Identity;

            var groups = items
                .OfType<GroupItem>()
                .Where(group => Applies(timeline, group, frame))
                .ToArray();

            if (groups.Length == 0)
                return Matrix4x4.Identity;

            var transform = Matrix4x4.Identity;
            var current = item;

            for (var depth = 0; depth < groups.Length; depth++)
            {
                if (FindParent(groups, current) is not { } parent)
                    break;

                var parentTime = new FrameContext(
                    frame - parent.Frame, Math.Max(1, parent.Length), fps);

                transform *= ItemPlacement.GetWorldMatrix(parent, parentTime);
                current = parent;
            }

            return transform;
        }

        private static bool Applies(Timeline timeline, GroupItem group, int frame)
        {
            if (!LayerVisibility.IsShown(timeline, group) || group.GroupRange <= 0)
                return false;

            // 「画像を合成」を入れたグループは、YMM4 が中身を1枚の絵にまとめてから
            // 動かす。そのときアイテム1つ1つは動かされないので、ここでも動かさない。
            if (group.IsComposite)
                return false;

            return frame >= group.Frame && frame < group.Frame + group.Length;
        }

        // レイヤー範囲に入っているグループのうち、いちばん近い（下にある）もの。
        private static GroupItem? FindParent(GroupItem[] groups, IItem item)
        {
            GroupItem? found = null;

            foreach (var group in groups)
            {
                if (ReferenceEquals(group, item))
                    continue;

                if (item.Layer <= group.Layer || item.Layer > group.Layer + group.GroupRange)
                    continue;

                // 「同一グループのみ」なら、同じグループ番号のアイテムだけを動かす。
                if (group.IsGroupOnly && group.Group != item.Group)
                    continue;

                if (found is null || group.Layer > found.Layer)
                    found = group;
            }

            return found;
        }
    }
}
