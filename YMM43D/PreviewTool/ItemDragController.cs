using System.Numerics;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// 3Dプレビュー上でアイテムを掴んで動かします。
    /// </summary>
    /// <remarks>
    /// 視線に垂直な面の上を滑らせます。掴んだ点が指の下から離れないので、
    /// カメラをどの向きにしていても、マウスの動きと絵の動きが一致します。
    /// <para>
    /// 面の向きはカメラ任せなので、斜めから見ているときは奥行きも変わります。
    /// 正面から見ているときは X・Y だけが動きます。
    /// </para>
    /// </remarks>
    internal sealed class ItemDragController
    {
        private IVideoItem? target;
        private Vector3 planePoint;
        private Vector3 planeNormal;
        private Vector3 anchor;

        /// <summary>いま掴んでいるアイテム。掴んでいなければ <c>null</c>。</summary>
        public IVideoItem? Target => target;

        public bool IsDragging => target is not null;

        /// <summary>
        /// アイテムを掴みます。
        /// </summary>
        /// <param name="item">掴むアイテム。</param>
        /// <param name="world">そのアイテムを描くのに使ったワールド行列。</param>
        /// <param name="ray">掴んだ位置から伸ばした視線。</param>
        /// <param name="viewDirection">カメラが向いている方向。</param>
        public void Begin(IVideoItem item, in Matrix4x4 world, in PickRay ray, in Vector3 viewDirection)
        {
            target = item;
            planePoint = world.Translation;
            planeNormal = -viewDirection;
            anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;
        }

        /// <summary>
        /// 掴んだ点を、いまの視線の先まで動かします。
        /// </summary>
        /// <returns>アイテムを動かしたら <c>true</c>。</returns>
        public bool Update(in PickRay ray)
        {
            if (target is not { } item)
                return false;

            if (ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            var shift = hit - anchor;
            if (shift == Vector3.Zero)
                return false;

            anchor = hit;

            // YMM4 の座標はピクセル。Y は画面と同じく下向きが正。
            item.X.Nudge(WorldScale.ToPixels(shift.X));
            item.Y.Nudge(-WorldScale.ToPixels(shift.Y));
            item.Z.Nudge(WorldScale.ToPixels(shift.Z));

            return true;
        }

        public void End() => target = null;
    }
}
