using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct DragTarget(IVideoItem Item, EditScope Scope, FrameContext Time);

    internal sealed class ItemDragController
    {
        private IReadOnlyList<DragTarget> targets = [];
        private ISceneMarkerSource? marker;
        private FrameContext time;
        private GizmoHandle handle;
        private EditScope scope;
        private GizmoSpace space;

        private Vector3 planePoint;
        private Vector3 planeNormal;
        private Vector3 anchor;

        private Vector3 axis;
        private float axisPosition;

        private Vector3 turnRight;
        private Vector3 turnUp;
        private float lastAngle;

        private Vector3 startPosition;
        private float startRotation;

        private Vector3 rawShift;
        private Vector3 appliedShift;
        private float rawTurn;
        private float appliedTurn;

        private float scaleReach;
        private float appliedRatio;
        private float[] startZooms = [];

        private ISceneRotation3D? turning;
        private Matrix4x4 turnStart;
        private Matrix4x4 turnFrameInverse;
        private Vector3 turnAngles;

        public GizmoHandle Handle => handle;

        public bool IsDragging => targets.Count > 0 || marker is not null;

        public bool BeginMarker(
            ISceneMarkerSource source,
            in FrameContext itemTime,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection,
            in EditScope edit)
        {
            turning = null;

            if (TransformGizmo.IsTurn(grabbed))
                grabbed = GizmoHandle.Free;

            if (!Grab(gizmo, ref grabbed, ray, viewDirection))
                return false;

            marker = source;
            targets = [];
            startZooms = [];
            time = itemTime;
            handle = grabbed;
            scope = edit;
            space = GizmoSpace.World;
            startPosition = source.GetMarker(itemTime).Position;
            startRotation = 0f;

            return true;
        }

        public bool Begin(
            IReadOnlyList<DragTarget> items,
            IVideoItem primary,
            in FrameContext primaryTime,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection,
            GizmoSpace gizmoSpace)
        {
            if (items.Count == 0)
                return false;

            space = gizmoSpace;
            turning = TransformGizmo.IsTurn(grabbed) && items.Count == 1
                ? ItemRotation.Find(primary)
                : null;

            if (turning is { } rotation)
                PrepareTurn(rotation, primary, primaryTime);

            if (!Grab(gizmo, ref grabbed, ray, viewDirection))
                return false;

            marker = null;
            targets = items;
            startZooms = [.. items.Select(one => one.Item.Zoom.GetFloat(one.Time))];
            time = primaryTime;
            handle = grabbed;
            scope = EditScope.Whole;
            startPosition = WorldScale.ToWorldPosition(
                primary.X.GetFloat(primaryTime), primary.Y.GetFloat(primaryTime), primary.Z.GetFloat(primaryTime));
            startRotation = primary.Rotation.GetFloat(primaryTime);

            return true;
        }

        private void PrepareTurn(ISceneRotation3D rotation, IVideoItem item, in FrameContext itemTime)
        {
            turnAngles = new Vector3(
                rotation.RotationX.GetFloat(itemTime),
                rotation.RotationY.GetFloat(itemTime),
                rotation.RotationZ.GetFloat(itemTime));

            turnStart = rotation.ToOrientation(turnAngles.X, turnAngles.Y, turnAngles.Z);

            var frame = Matrix4x4.CreateRotationZ(
                -float.DegreesToRadians(item.Rotation.GetFloat(itemTime)));

            if (!Matrix4x4.Invert(frame, out turnFrameInverse))
                turnFrameInverse = Matrix4x4.Identity;
        }

        private bool Grab(
            in TransformGizmo gizmo, ref GizmoHandle grabbed, in PickRay ray, in Vector3 viewDirection)
        {
            planePoint = gizmo.Origin;
            rawShift = appliedShift = Vector3.Zero;
            rawTurn = appliedTurn = 0f;

            if (TransformGizmo.IsAxis(grabbed))
            {
                axis = gizmo.Axis(grabbed);

                if (TransformGizmo.ClosestOnAxis(ray, gizmo.Origin, axis) is not { } position)
                    return false;

                axisPosition = position;

                return true;
            }

            if (TransformGizmo.IsPlane(grabbed))
            {
                planeNormal = gizmo.PlaneNormal(grabbed);
                anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;

                return true;
            }

            if (grabbed is GizmoHandle.ScaleAll)
            {
                planeNormal = -viewDirection;
                anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;
                scaleReach = Vector3.Distance(anchor, planePoint);
                appliedRatio = 1f;

                return scaleReach >= 1e-4f;
            }

            if (TransformGizmo.IsTurn(grabbed))
            {
                if (turning is null)
                    grabbed = GizmoHandle.RotateZ;

                axis = turning is not null ? gizmo.TurnAxis(grabbed) : Vector3.UnitZ;
                (turnRight, turnUp) = TransformGizmo.PlaneBasis(axis);

                if (GetAngle(ray, gizmo.Origin) is not { } angle)
                    return false;

                lastAngle = angle;

                return true;
            }

            grabbed = GizmoHandle.Free;
            planeNormal = -viewDirection;
            anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;

            return true;
        }

        public bool Update(in PickRay ray, in SnapGrid snap)
        {
            if (!IsDragging)
                return false;

            if (TransformGizmo.IsTurn(handle))
                return Turn(ray, snap);

            if (handle is GizmoHandle.ScaleAll)
                return Resize(ray, snap);

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

            foreach (var target in targets)
                target.Scope.NudgePosition(target.Item.X, target.Item.Y, target.Item.Z, delta);

            return true;
        }

        public void End()
        {
            targets = [];
            startZooms = [];
            marker = null;
            turning = null;
            handle = GizmoHandle.None;
        }

        private bool Resize(in PickRay ray, in SnapGrid snap)
        {
            if (startZooms.Length == 0 || ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            var reach = Vector3.Distance(hit, planePoint);
            var ratio = snap.SnapScale(startZooms[0], MathF.Max(reach / scaleReach, 0f));

            if (ratio == appliedRatio)
                return false;

            for (var i = 0; i < targets.Count && i < startZooms.Length; i++)
            {
                var (item, itemScope) = (targets[i].Item, targets[i].Scope);

                itemScope.Nudge(item.Zoom, startZooms[i] * (ratio - appliedRatio));
            }

            appliedRatio = ratio;

            return true;
        }

        private bool Follow(in PickRay ray, out Vector3 step)
        {
            step = Vector3.Zero;

            if (TransformGizmo.IsAxis(handle))
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

            if (turning is null)
            {
                rawTurn -= float.RadiansToDegrees(delta);

                var flat = snap.SnapTurn(startRotation, rawTurn);
                var change = flat - appliedTurn;

                if (change == 0f)
                    return false;

                appliedTurn = flat;

                foreach (var target in targets)
                    target.Scope.Nudge(target.Item.Rotation, change);

                return true;
            }

            rawTurn += float.RadiansToDegrees(delta);

            var wanted = snap.SnapTurn(0f, rawTurn);

            if (wanted == appliedTurn)
                return false;

            appliedTurn = wanted;

            return ApplyTurn(wanted);
        }

        private bool ApplyTurn(float degrees)
        {
            if (turning is not { } rotation)
                return false;

            var moved = GizmoTurn.Apply(turnStart, turnFrameInverse, axis, handle, degrees, space);
            var angles = rotation.ToAngles(moved);

            var change = new Vector3(
                Rotation3D.Wrap(angles.X - turnAngles.X),
                Rotation3D.Wrap(angles.Y - turnAngles.Y),
                Rotation3D.Wrap(angles.Z - turnAngles.Z));

            if (MathF.Abs(change.X) < 1e-4f && MathF.Abs(change.Y) < 1e-4f && MathF.Abs(change.Z) < 1e-4f)
                return false;

            turnAngles = angles;

            foreach (var target in targets)
            {
                target.Scope.Nudge(rotation.RotationX, change.X);
                target.Scope.Nudge(rotation.RotationY, change.Y);
                target.Scope.Nudge(rotation.RotationZ, change.Z);
            }

            return true;
        }

        private float? GetAngle(in PickRay ray, in Vector3 origin)
        {
            var angle = GizmoTurn.Measure(ray, origin, axis, turnRight, turnUp);

            return float.IsNaN(angle) ? null : angle;
        }
    }
}
