using System.Numerics;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class ItemDragController
    {
        private IVideoItem? target;
        private ISceneMarkerSource? marker;
        private FrameContext markerTime;
        private GizmoHandle handle;
        private EditScope scope;

        private Vector3 planePoint;
        private Vector3 planeNormal;
        private Vector3 anchor;

        private Vector3 axis;
        private float axisPosition;

        private float lastAngle;

        public IVideoItem? Target => target;

        public GizmoHandle Handle => handle;

        public bool IsDragging => target is not null || marker is not null;

        public bool BeginMarker(
            ISceneMarkerSource source,
            in FrameContext itemTime,
            in Vector3 origin,
            in PickRay ray,
            in Vector3 viewDirection,
            in EditScope edit)
        {
            planePoint = origin;
            planeNormal = -viewDirection;
            anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;

            marker = source;
            markerTime = itemTime;
            handle = GizmoHandle.Free;
            scope = edit;

            return true;
        }

        public bool Begin(
            IVideoItem item,
            in Vector3 origin,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection,
            in EditScope edit)
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
            scope = edit;

            return true;
        }

        public bool Update(in PickRay ray)
        {
            if (marker is { } source)
                return MoveMarker(source, ray);

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
            marker = null;
            handle = GizmoHandle.None;
        }

        private bool MoveMarker(ISceneMarkerSource source, in PickRay ray)
        {
            if (ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            var shift = hit - anchor;
            if (shift == Vector3.Zero)
                return false;

            anchor = hit;
            source.MoveMarker(shift, markerTime, scope);

            return true;
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

            var delta = angle - lastAngle;
            delta -= MathF.Tau * MathF.Round(delta / MathF.Tau);

            if (delta == 0f)
                return false;

            lastAngle = angle;

            scope.Nudge(item.Rotation, -delta * 180f / MathF.PI);

            return true;
        }

        private void Shift(IVideoItem item, in Vector3 shift)
        {
            scope.Nudge(item.X, WorldScale.ToPixels(shift.X));
            scope.Nudge(item.Y, -WorldScale.ToPixels(shift.Y));
            scope.Nudge(item.Z, WorldScale.ToPixels(shift.Z));
        }

        private static float? GetAngle(in PickRay ray, in Vector3 origin)
        {
            if (ray.IntersectPlane(origin, Vector3.UnitZ) is not { } hit)
                return null;

            var offset = hit - origin;

            return MathF.Atan2(offset.Y, offset.X);
        }
    }
}
