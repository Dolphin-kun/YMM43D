using System.Numerics;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// 3Dプレビュー上でアイテムを掴んで動かします。
    /// </summary>
    /// <remarks>
    /// 案内の矢印を掴めばその軸だけに沿って動き、輪を掴めば回ります。何も掴まずに
    /// アイテムそのものをドラッグした場合は、視線に垂直な面の上を滑ります。
    /// </remarks>
    internal sealed class ItemDragController
    {
        private IVideoItem? target;
        private GizmoHandle handle;

        // 面に沿って動かすとき用。
        private Vector3 planePoint;
        private Vector3 planeNormal;
        private Vector3 anchor;

        // 軸に沿って動かすとき用。
        private Vector3 axis;
        private float axisPosition;

        // 回すとき用。
        private float lastAngle;

        /// <summary>いま掴んでいるアイテム。掴んでいなければ <c>null</c>。</summary>
        public IVideoItem? Target => target;

        /// <summary>いま掴んでいる案内の部分。</summary>
        public GizmoHandle Handle => handle;

        public bool IsDragging => target is not null;

        /// <summary>
        /// アイテムを掴みます。
        /// </summary>
        /// <param name="item">掴むアイテム。</param>
        /// <param name="origin">アイテムの中心（ワールド空間）。</param>
        /// <param name="grabbed">掴んだ案内の部分。<see cref="GizmoHandle.Free"/> なら面に沿って動きます。</param>
        /// <param name="ray">掴んだ位置から伸ばした視線。</param>
        /// <param name="viewDirection">カメラが向いている方向。</param>
        /// <returns>掴めたら <c>true</c>。</returns>
        public bool Begin(
            IVideoItem item,
            in Vector3 origin,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection)
        {
            planePoint = origin;

            switch (grabbed)
            {
                case GizmoHandle.MoveX or GizmoHandle.MoveY or GizmoHandle.MoveZ:
                    axis = TransformGizmo.AxisDirection(grabbed);

                    if (TransformGizmo.ClosestOnAxis(ray, origin, axis) is not { } position)
                        return false;

                    axisPosition = position;
                    break;

                case GizmoHandle.RotateZ:
                    if (GetAngle(ray, origin) is not { } angle)
                        return false;

                    lastAngle = angle;
                    break;

                default:
                    grabbed = GizmoHandle.Free;
                    planeNormal = -viewDirection;
                    anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;
                    break;
            }

            target = item;
            handle = grabbed;

            return true;
        }

        /// <summary>
        /// 掴んだ所を、いまの視線の先まで動かします。
        /// </summary>
        /// <returns>アイテムを動かしたら <c>true</c>。</returns>
        public bool Update(in PickRay ray)
        {
            if (target is not { } item)
                return false;

            return handle switch
            {
                GizmoHandle.MoveX or GizmoHandle.MoveY or GizmoHandle.MoveZ => MoveAlongAxis(item, ray),
                GizmoHandle.RotateZ => Rotate(item, ray),
                _ => MoveOnPlane(item, ray),
            };
        }

        public void End()
        {
            target = null;
            handle = GizmoHandle.None;
        }

        private bool MoveAlongAxis(IVideoItem item, in PickRay ray)
        {
            if (TransformGizmo.ClosestOnAxis(ray, planePoint, axis) is not { } position)
                return false;

            var delta = position - axisPosition;
            if (delta == 0f)
                return false;

            axisPosition = position;
            Shift(item, axis * delta);

            return true;
        }

        private bool MoveOnPlane(IVideoItem item, in PickRay ray)
        {
            if (ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            var shift = hit - anchor;
            if (shift == Vector3.Zero)
                return false;

            anchor = hit;
            Shift(item, shift);

            return true;
        }

        private bool Rotate(IVideoItem item, in PickRay ray)
        {
            if (GetAngle(ray, planePoint) is not { } angle)
                return false;

            // 1周をまたぐと角度が跳ぶ。近い方の差を取る。
            var delta = angle - lastAngle;
            delta -= MathF.Tau * MathF.Round(delta / MathF.Tau);

            if (delta == 0f)
                return false;

            lastAngle = angle;

            // YMM4 の回転角は時計回りが正。3D 空間の反時計回りとは向きが逆。
            item.Rotation.Nudge(-delta * 180f / MathF.PI);

            return true;
        }

        private static void Shift(IVideoItem item, in Vector3 shift)
        {
            // YMM4 の座標はピクセル。Y は画面と同じく下向きが正。
            item.X.Nudge(WorldScale.ToPixels(shift.X));
            item.Y.Nudge(-WorldScale.ToPixels(shift.Y));
            item.Z.Nudge(WorldScale.ToPixels(shift.Z));
        }

        /// <summary>輪の上で、いまどの向きを指しているか。</summary>
        private static float? GetAngle(in PickRay ray, in Vector3 origin)
        {
            if (ray.IntersectPlane(origin, Vector3.UnitZ) is not { } hit)
                return null;

            var offset = hit - origin;

            return MathF.Atan2(offset.Y, offset.X);
        }
    }
}
