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
        private MeshTransformer? transformer;
        private MeshSnapper? snapper;
        private MeshSelection? selection;
        private Matrix4x4 toWorld;
        private Matrix4x4 orientation = Matrix4x4.Identity;

        private Vector3 pivotWorld;
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
        private Matrix4x4 toLocal;

        public bool IsDragging => parameter is not null;

        public GizmoHandle Handle { get; private set; }

        public SnapTargets Snapped { get; private set; }

        public ProportionalEdit? Proportional => transformer?.Proportional;

        public bool Begin(
            in MeshEditTarget target,
            MeshSelection chosen,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection,
            PivotPoint pivot,
            in Vector3 cursor,
            ProportionalEdit? proportional,
            MeshSnapper? snaps)
        {
            if (chosen.IsEmpty)
                return false;

            snapper = snaps;

            return Start(target, chosen, pivot, cursor, proportional, gizmo, grabbed, ray, viewDirection);
        }

        public bool BeginInflate(in MeshEditTarget target, MeshSelection chosen, in Vector3 handle, in PickRay ray, ProportionalEdit? proportional)
        {
            if (chosen.IsEmpty)
                return false;

            var origin = MeshPivot.Median(target.Mesh, target.ToWorld, chosen.Vertices);

            if (!Start(target, chosen, PivotPoint.Median, origin, proportional, new TransformGizmo(handle, 1f, Matrix4x4.Identity), GizmoHandle.Inflate, ray, Vector3.Zero))
                return false;

            pivotWorld = origin;
            axis = handle - pivotWorld;

            if (axis.LengthSquared() < 1e-12f
                || TransformGizmo.ClosestOnAxis(ray, pivotWorld, axis = Vector3.Normalize(axis)) is not { } at)
            {
                return Fail();
            }

            axisPosition = at;
            normals = mesh!.PointNormals();
            appliedSwell = 0f;

            return true;
        }

        public bool BeginWhole(in MeshEditTarget target, in TransformGizmo gizmo, GizmoHandle grabbed, in PickRay ray, in Vector3 viewDirection)
        {
            var everything = new MeshSelection();

            everything.Bind(target.Parameter.ShownMesh());
            everything.SelectAll();

            snapper = null;

            return Start(target, everything, PivotPoint.Cursor, Vector3.Transform(Vector3.Zero, target.ToWorld), null, gizmo, grabbed, ray, viewDirection);
        }

        private bool Start(
            in MeshEditTarget target,
            MeshSelection chosen,
            PivotPoint pivot,
            in Vector3 cursor,
            ProportionalEdit? proportional,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in Vector3 viewDirection)
        {
            if (grabbed is GizmoHandle.None
                || target.Item.IsLocked
                || !Matrix4x4.Invert(target.ToWorld, out toLocal))
            {
                return false;
            }

            var built = target.Parameter.ShownMesh();

            chosen.Bind(built);

            if (chosen.IsEmpty)
                return false;

            parameter = target.Parameter;
            mesh = built;
            selection = chosen;
            toWorld = target.ToWorld;
            orientation = gizmo.Orientation;
            transformer = new MeshTransformer(built, target.ToWorld, chosen, pivot, cursor, proportional);
            pivotWorld = gizmo.Origin;
            Handle = grabbed;
            Snapped = SnapTargets.None;

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

            Handle = GizmoHandle.Free;
            planeNormal = -viewDirection;
            anchor = ray.IntersectPlane(pivotWorld, planeNormal) ?? pivotWorld;

            return true;
        }

        private bool Fail()
        {
            End();

            return false;
        }

        public bool Update(in PickRay ray, in Vector2 screen, in ViewSnapshot view, in SnapSettings snap)
        {
            if (parameter is not { } target || mesh is not { } built || transformer is null)
                return false;

            if (Handle is GizmoHandle.Inflate)
                return Swell(ray, snap, target, built);

            if (TransformGizmo.IsTurn(Handle))
                return Turn(ray, snap, target, built);

            if (TransformGizmo.IsScale(Handle) || Handle is GizmoHandle.ScaleAll)
                return Resize(ray, snap, target, built);

            return Shift(ray, screen, view, snap, target, built);
        }

        public void Widen(float ratio)
        {
            if (transformer?.Proportional is { } shaped)
                transformer.Proportional = shaped with { Radius = MathF.Max(shaped.Radius * ratio, ProportionalEdit.LeastRadius) };
        }

        public void End()
        {
            parameter = null;
            mesh = null;
            transformer = null;
            snapper = null;
            selection = null;
            normals = [];
            Handle = GizmoHandle.None;
            Snapped = SnapTargets.None;
        }

        private bool Shift(in PickRay ray, in Vector2 screen, in ViewSnapshot view, in SnapSettings snap, Shape3DParameter target, MeshTopology built)
        {
            Vector3 step;

            if (TransformGizmo.IsAxis(Handle))
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

            Snapped = SnapTargets.None;

            Vector3 wanted;

            if (snap.SnapsToElements && snapper?.Find(screen, view, snap.Targets) is { } found)
            {
                Snapped = found.Kind;
                wanted = Constrain(found.Point - BaseFor(snap.Base, found.Point));
            }
            else
            {
                wanted = snap.SnapsToIncrement ? Constrain(snap.Grid.SnapShift(pivotWorld, rawShift)) : rawShift;
            }

            if (wanted == appliedShift)
                return false;

            appliedShift = wanted;

            return Store(target, built, transformer!.Translate(appliedShift));
        }

        private Vector3 Constrain(in Vector3 shift)
        {
            if (TransformGizmo.IsAxis(Handle))
                return axis * Vector3.Dot(shift, axis);

            return shift - planeNormal * Vector3.Dot(shift, planeNormal);
        }

        private Vector3 BaseFor(SnapBase wanted, in Vector3 target)
        {
            if (selection is null || transformer is null)
                return pivotWorld;

            switch (wanted)
            {
                case SnapBase.Active:
                    return MeshPivot.ActiveCenter(transformer.Mesh, toWorld, selection) ?? pivotWorld;

                case SnapBase.Closest:
                    var nearest = float.MaxValue;
                    var found = pivotWorld;

                    foreach (var vertex in selection.Vertices)
                    {
                        var placed = transformer.Placed[vertex];
                        var distance = Vector3.DistanceSquared(placed, target);

                        if (distance >= nearest)
                            continue;

                        nearest = distance;
                        found = placed;
                    }

                    return found;

                default:
                    return pivotWorld;
            }
        }

        private bool Turn(in PickRay ray, in SnapSettings snap, Shape3DParameter target, MeshTopology built)
        {
            var angle = GizmoTurn.Measure(ray, pivotWorld, axis, turnRight, turnUp);

            if (float.IsNaN(angle))
                return false;

            var step = angle - lastAngle;

            step -= MathF.Tau * MathF.Round(step / MathF.Tau);
            lastAngle = angle;
            rawTurn += float.RadiansToDegrees(step);

            var wanted = snap.SnapsToIncrement ? snap.Grid.SnapTurn(0f, rawTurn) : rawTurn;

            if (wanted == appliedTurn)
                return false;

            appliedTurn = wanted;

            return Store(target, built, transformer!.Rotate(axis, float.DegreesToRadians(appliedTurn)));
        }

        private bool Resize(in PickRay ray, in SnapSettings snap, Shape3DParameter target, MeshTopology built)
        {
            float ratio;

            if (Handle is GizmoHandle.ScaleAll)
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

            if (snap.SnapsToIncrement)
                ratio = snap.Grid.SnapScale(100f, ratio);

            if (ratio == appliedRatio)
                return false;

            appliedRatio = ratio;

            var scale = Handle switch
            {
                GizmoHandle.ScaleX => new Vector3(ratio, 1f, 1f),
                GizmoHandle.ScaleY => new Vector3(1f, ratio, 1f),
                GizmoHandle.ScaleZ => new Vector3(1f, 1f, ratio),
                _ => new Vector3(ratio),
            };

            return Store(target, built, transformer!.Scale(scale, orientation));
        }

        private bool Swell(in PickRay ray, in SnapSettings snap, Shape3DParameter target, MeshTopology built)
        {
            if (TransformGizmo.ClosestOnAxis(ray, pivotWorld, axis) is not { } at)
                return false;

            var reach = snap.SnapsToIncrement ? snap.Grid.SnapLength(at - axisPosition) : at - axisPosition;
            var amount = Vector3.TransformNormal(axis * reach, toLocal).Length() * MathF.Sign(reach);

            if (amount == appliedSwell)
                return false;

            appliedSwell = amount;

            return Store(target, built, transformer!.Push(normals, amount));
        }

        private bool Store(Shape3DParameter target, MeshTopology built, Vector3[] points)
        {
            var moved = built.WithPoints(points);

            mesh = target.HasMesh ? target.StorePoints(moved) : target.StoreMesh(moved);

            return true;
        }
    }
}
