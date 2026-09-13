using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct DragTarget(IVideoItem Item, EditScope Scope);

    internal sealed class ItemDragController
    {
        private IReadOnlyList<DragTarget> targets = [];
        private ISceneMarkerSource? marker;
        private FrameContext time;
        private GizmoHandle handle;
        private EditScope scope;

        private Vector3 planePoint;
        private Vector3 planeNormal;
        private Vector3 anchor;

        private Vector3 axis;
        private float axisPosition;

        private float lastAngle;

        private Vector3 startPosition;
        private float startRotation;

        private Vector3 rawShift;
        private Vector3 appliedShift;
        private float rawTurn;
        private float appliedTurn;

        public GizmoHandle Handle => handle;

        public bool IsDragging => targets.Count > 0 || marker is not null;

        public bool BeginMarker(
            ISceneMarkerSource source,
            in FrameContext itemTime,
            in Vector3 origin,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection,
            in EditScope edit)
        {
            if (grabbed == GizmoHandle.RotateZ)
                grabbed = GizmoHandle.Free;

            if (!Grab(origin, ref grabbed, ray, viewDirection))
                return false;

            marker = source;
            targets = [];
            time = itemTime;
            handle = grabbed;
            scope = edit;
            startPosition = source.GetMarker(itemTime).Position;
            startRotation = 0f;

            return true;
        }

        public bool Begin(
            IReadOnlyList<DragTarget> items,
            IVideoItem primary,
            in FrameContext primaryTime,
            in Vector3 origin,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection)
        {
            if (items.Count == 0 || !Grab(origin, ref grabbed, ray, viewDirection))
                return false;

            marker = null;
            targets = items;
            time = primaryTime;
            handle = grabbed;
            scope = EditScope.Whole;
            startPosition = WorldScale.ToWorldPosition(
                primary.X.GetFloat(primaryTime), primary.Y.GetFloat(primaryTime), primary.Z.GetFloat(primaryTime));
            startRotation = primary.Rotation.GetFloat(primaryTime);

            return true;
        }

        private bool Grab(in Vector3 origin, ref GizmoHandle grabbed, in PickRay ray, in Vector3 viewDirection)
        {
            planePoint = origin;
            rawShift = appliedShift = Vector3.Zero;
            rawTurn = appliedTurn = 0f;

            switch (grabbed)
            {
                case GizmoHandle.MoveX or GizmoHandle.MoveY or GizmoHandle.MoveZ:
                    axis = TransformGizmo.AxisDirection(grabbed);

                    if (TransformGizmo.ClosestOnAxis(ray, origin, axis) is not { } position)
                        return false;

                    axisPosition = position;
                    return true;

                case GizmoHandle.RotateZ:
                    if (GetAngle(ray, origin) is not { } angle)
                        return false;

                    lastAngle = angle;
                    return true;

                default:
                    grabbed = GizmoHandle.Free;
                    planeNormal = -viewDirection;
                    anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;
                    return true;
            }
        }

        public bool Update(in PickRay ray, in SnapGrid snap)
        {
            if (!IsDragging)
                return false;

            if (handle == GizmoHandle.RotateZ)
                return Turn(ray, snap);

            if (!Follow(ray, out var step))
                return false;

            rawShift += step;

            var wanted = snap.SnapShift(startPosition, rawShift);
            var delta = wanted - appliedShift;

            if (delta == Vector3.Zero)
                return false;

            appliedShift = wanted;

            if (marker is { } source)
            {
                source.MoveMarker(delta, time, scope);
                return true;
            }

            foreach (var (item, itemScope) in targets)
                itemScope.NudgePosition(item.X, item.Y, item.Z, delta);

            return true;
        }

        public void End()
        {
            targets = [];
            marker = null;
            handle = GizmoHandle.None;
        }

        private bool Follow(in PickRay ray, out Vector3 step)
        {
            step = Vector3.Zero;

            if (handle is GizmoHandle.MoveX or GizmoHandle.MoveY or GizmoHandle.MoveZ)
            {
                if (TransformGizmo.ClosestOnAxis(ray, planePoint, axis) is not { } position)
                    return false;

                step = axis * (position - axisPosition);
                axisPosition = position;
                return true;
            }

            if (ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            step = hit - anchor;
            anchor = hit;
            return true;
        }

        private bool Turn(in PickRay ray, in SnapGrid snap)
        {
            if (GetAngle(ray, planePoint) is not { } angle)
                return false;

            var delta = angle - lastAngle;
            delta -= MathF.Tau * MathF.Round(delta / MathF.Tau);
            lastAngle = angle;

            rawTurn -= float.RadiansToDegrees(delta);

            var wanted = snap.SnapTurn(startRotation, rawTurn);
            var change = wanted - appliedTurn;

            if (change == 0f)
                return false;

            appliedTurn = wanted;

            foreach (var (item, itemScope) in targets)
                itemScope.Nudge(item.Rotation, change);

            return true;
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
