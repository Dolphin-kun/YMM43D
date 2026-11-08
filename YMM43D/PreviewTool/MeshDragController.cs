using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;

namespace YMM43D.PreviewTool
{
    internal sealed class MeshDragController
    {
        private Shape3DParameter? parameter;
        private MeshTopology? mesh;
        private int[] chosen = [];
        private Vector3[] startPoints = [];

        private Matrix4x4 toLocal = Matrix4x4.Identity;
        private Vector3 pivotLocal;
        private Vector3 pivotWorld;
        private GizmoHandle handle;

        private Vector3 axis;
        private float axisPosition;
        private Vector3 planeNormal;
        private Vector3 anchor;

        private Vector3 turnRight;
        private Vector3 turnUp;
        private float lastAngle;
        private float rawTurn;
        private float appliedTurn;

        private Vector3 rawShift;
        private Vector3 appliedShift;

        private float scaleStart;
        private float scaleSpan;
        private float appliedRatio;

        private Vector3[] normals = [];
        private float appliedSwell;

        public bool IsDragging => parameter is not null;

        public GizmoHandle Handle => handle;

        public bool Begin(
            in MeshEditTarget target,
            IReadOnlyCollection<int> selection,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection)
            => Start(target, selection, gizmo, grabbed, ray, viewDirection);

        public bool BeginInflate(
            in MeshEditTarget target,
            IReadOnlyCollection<int> selection,
            in Vector3 handle,
            in PickRay ray)
        {
            if (!Start(target, selection, new TransformGizmo(handle, 1f, Matrix4x4.Identity), GizmoHandle.Inflate, ray, Vector3.Zero))
                return false;

            pivotWorld = Vector3.Transform(mesh!.Middle(chosen), target.ToWorld);
            axis = handle - pivotWorld;

            if (axis.LengthSquared() < 1e-12f
                || TransformGizmo.ClosestOnAxis(ray, pivotWorld, axis = Vector3.Normalize(axis)) is not { } at)
            {
                return Fail();
            }

            axisPosition = at;
            normals = mesh.PointNormals();
            appliedSwell = 0f;

            return true;
        }

        public bool BeginWhole(
            in MeshEditTarget target,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection)
            => Start(target, null, gizmo, grabbed, ray, viewDirection);

        private bool Start(
            in MeshEditTarget target,
            IReadOnlyCollection<int>? selection,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection)
        {
            if (selection is { Count: 0 }
                || grabbed is GizmoHandle.None
                || target.Item.IsLocked
                || !Matrix4x4.Invert(target.ToWorld, out toLocal))
            {
                return false;
            }

            var built = target.Parameter.ShownMesh();

            chosen = selection is null
                ? [.. Enumerable.Range(0, built.Points.Length)]
                : [.. selection.Where(one => one >= 0 && one < built.Points.Length)];

            if (chosen.Length == 0)
                return false;

            parameter = target.Parameter;
            mesh = built;
            startPoints = [.. built.Points];
            pivotLocal = selection is null ? Vector3.Zero : built.Middle(chosen);
            pivotWorld = gizmo.Origin;
            handle = grabbed;

            rawShift = appliedShift = Vector3.Zero;
            rawTurn = appliedTurn = 0f;
            appliedRatio = 1f;

            if (TransformGizmo.IsAxis(grabbed))
            {
                axis = gizmo.Axis(grabbed);

                if (TransformGizmo.ClosestOnAxis(ray, pivotWorld, axis) is not { } at)
                    return Fail();

                axisPosition = at;

                return true;
            }

            if (TransformGizmo.IsPlane(grabbed))
            {
                planeNormal = gizmo.PlaneNormal(grabbed);
                anchor = ray.IntersectPlane(pivotWorld, planeNormal) ?? pivotWorld;

                return true;
            }

            if (TransformGizmo.IsTurn(grabbed))
            {
                axis = gizmo.TurnAxis(grabbed);
                (turnRight, turnUp) = TransformGizmo.PlaneBasis(axis);

                var angle = GizmoTurn.Measure(ray, pivotWorld, axis, turnRight, turnUp);

                if (float.IsNaN(angle))
                    return Fail();

                lastAngle = angle;

                return true;
            }

            if (grabbed is GizmoHandle.Inflate)
                return true;

            if (TransformGizmo.IsScale(grabbed))
            {
                axis = gizmo.Axis(grabbed);

                return Stretch3D.TryReach(ray, pivotWorld, axis, out scaleSpan) || Fail();
            }

            if (grabbed is GizmoHandle.ScaleAll)
            {
                planeNormal = -viewDirection;
                anchor = ray.IntersectPlane(pivotWorld, planeNormal) ?? pivotWorld;
                scaleStart = Vector3.Distance(anchor, pivotWorld);
                scaleSpan = Stretch3D.CenterSpan(gizmo.Scale);

                return true;
            }

            handle = GizmoHandle.Free;
            planeNormal = -viewDirection;
            anchor = ray.IntersectPlane(pivotWorld, planeNormal) ?? pivotWorld;

            return true;
        }

        private bool Fail()
        {
            End();

            return false;
        }

        public bool Update(in PickRay ray, in SnapGrid snap)
        {
            if (parameter is not { } target || mesh is not { } built)
                return false;

            if (handle is GizmoHandle.Inflate)
                return Swell(ray, snap, target, built);

            if (TransformGizmo.IsTurn(handle))
                return Turn(ray, snap, target, built);

            if (TransformGizmo.IsScale(handle) || handle is GizmoHandle.ScaleAll)
                return Resize(ray, snap, target, built);

            return Shift(ray, snap, target, built);
        }

        public void End()
        {
            parameter = null;
            mesh = null;
            chosen = [];
            startPoints = [];
            normals = [];
            handle = GizmoHandle.None;
        }

        private bool Shift(in PickRay ray, in SnapGrid snap, Shape3DParameter target, MeshTopology built)
        {
            Vector3 step;

            if (TransformGizmo.IsAxis(handle))
            {
                if (TransformGizmo.ClosestOnAxis(ray, pivotWorld, axis) is not { } at)
                    return false;

                step = axis * (at - axisPosition);
                axisPosition = at;
            }
            else
            {
                if (ray.IntersectPlane(pivotWorld, planeNormal) is not { } hit)
                    return false;

                step = hit - anchor;
                anchor = hit;
            }

            rawShift += step;

            var wanted = snap.SnapShift(pivotWorld, rawShift);

            if (wanted == appliedShift)
                return false;

            appliedShift = wanted;

            return Apply(
                target, built, Matrix4x4.CreateTranslation(Vector3.TransformNormal(appliedShift, toLocal)));
        }

        private bool Turn(in PickRay ray, in SnapGrid snap, Shape3DParameter target, MeshTopology built)
        {
            var angle = GizmoTurn.Measure(ray, pivotWorld, axis, turnRight, turnUp);

            if (float.IsNaN(angle))
                return false;

            var step = angle - lastAngle;

            step -= MathF.Tau * MathF.Round(step / MathF.Tau);
            lastAngle = angle;
            rawTurn += float.RadiansToDegrees(step);

            var wanted = snap.SnapTurn(0f, rawTurn);

            if (wanted == appliedTurn)
                return false;

            appliedTurn = wanted;

            var change = Matrix4x4.CreateTranslation(-pivotLocal)
                * Matrix4x4.CreateFromAxisAngle(
                      TransformGizmo.TurnDirection(handle), float.DegreesToRadians(appliedTurn))
                * Matrix4x4.CreateTranslation(pivotLocal);

            return Apply(target, built, change);
        }

        private bool Resize(in PickRay ray, in SnapGrid snap, Shape3DParameter target, MeshTopology built)
        {
            float ratio;

            if (handle is GizmoHandle.ScaleAll)
            {
                if (ray.IntersectPlane(pivotWorld, planeNormal) is not { } hit)
                    return false;

                ratio = Stretch3D.CenterRatio(Vector3.Distance(hit, pivotWorld), scaleStart, scaleSpan);
            }
            else
            {
                ratio = Stretch3D.Ratio(ray, pivotWorld, axis, scaleSpan);

                if (float.IsNaN(ratio))
                    return false;
            }

            ratio = snap.SnapScale(100f, ratio);

            if (ratio == appliedRatio)
                return false;

            appliedRatio = ratio;

            var scale = handle switch
            {
                GizmoHandle.ScaleX => new Vector3(ratio, 1f, 1f),
                GizmoHandle.ScaleY => new Vector3(1f, ratio, 1f),
                GizmoHandle.ScaleZ => new Vector3(1f, 1f, ratio),
                _ => new Vector3(ratio),
            };

            var change = Matrix4x4.CreateTranslation(-pivotLocal)
                * Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateTranslation(pivotLocal);

            return Apply(target, built, change);
        }

        private bool Swell(in PickRay ray, in SnapGrid snap, Shape3DParameter target, MeshTopology built)
        {
            if (TransformGizmo.ClosestOnAxis(ray, pivotWorld, axis) is not { } at)
                return false;

            var reach = snap.SnapLength(at - axisPosition);
            var amount = Vector3.TransformNormal(axis * reach, toLocal).Length() * MathF.Sign(reach);

            if (amount == appliedSwell)
                return false;

            appliedSwell = amount;

            var points = (Vector3[])startPoints.Clone();

            foreach (var index in chosen)
                points[index] = startPoints[index] + normals[index] * amount;

            return Store(target, built, points);
        }

        private bool Apply(Shape3DParameter target, MeshTopology built, in Matrix4x4 change)
        {
            var points = (Vector3[])startPoints.Clone();

            foreach (var index in chosen)
                points[index] = Vector3.Transform(startPoints[index], change);

            return Store(target, built, points);
        }

        private bool Store(Shape3DParameter target, MeshTopology built, Vector3[] points)
        {
            var moved = built.WithPoints(points);

            mesh = target.HasMesh ? target.StorePoints(moved) : target.StoreMesh(moved);

            return true;
        }
    }
}
