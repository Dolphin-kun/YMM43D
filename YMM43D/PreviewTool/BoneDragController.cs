using System.Numerics;
using YMM43D.Commons;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool
{
    // 3Dプレビューで骨のつまみを掴んで回す。書き込む先はパラメータの「骨」と同じ値。
    internal sealed class BoneDragController
    {
        private ModelBonePose? pose;
        private Animation? target;
        private EditScope scope;
        private GizmoHandle handle;

        private Vector3 origin;
        private Vector3 axis;
        private Vector3 right;
        private Vector3 up;

        private float lastAngle;
        private float startValue;
        private float rawTurn;
        private float appliedTurn;

        public bool IsDragging => pose is not null;

        public GizmoHandle Handle => handle;

        public bool Begin(
            in PreviewBone bone,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            in PickRay ray,
            in EditScope edit)
        {
            if (grabbed is not (GizmoHandle.RotateX or GizmoHandle.RotateY or GizmoHandle.RotateZ))
                return false;

            var found = bone.Parameter.BoneFor(bone.Name);

            axis = gizmo.TurnAxis(grabbed);

            if (axis.LengthSquared() < 1e-12f)
                return false;

            origin = gizmo.Origin;
            (right, up) = Basis(axis);

            if (GetAngle(ray) is not { } angle)
                return false;

            pose = found;
            handle = grabbed;
            scope = edit;

            target = grabbed switch
            {
                GizmoHandle.RotateX => found.RotationX,
                GizmoHandle.RotateY => found.RotationY,
                _ => found.RotationZ,
            };

            startValue = target.GetFloat(bone.Time);
            lastAngle = angle;
            rawTurn = 0f;
            appliedTurn = 0f;

            return true;
        }

        public bool Update(in PickRay ray, in SnapGrid snap)
        {
            if (pose is null || target is null || GetAngle(ray) is not { } angle)
                return false;

            var delta = angle - lastAngle;
            delta -= MathF.Tau * MathF.Round(delta / MathF.Tau);
            lastAngle = angle;

            rawTurn += float.RadiansToDegrees(delta);

            var wanted = snap.SnapTurn(startValue, rawTurn);
            var change = wanted - appliedTurn;

            if (change == 0f)
                return false;

            appliedTurn = wanted;
            scope.Nudge(target, change);

            return true;
        }

        public void End()
        {
            pose = null;
            target = null;
            handle = GizmoHandle.None;
        }

        private float? GetAngle(in PickRay ray)
        {
            if (ray.IntersectPlane(origin, axis) is not { } hit)
                return null;

            var offset = hit - origin;

            return MathF.Atan2(Vector3.Dot(offset, up), Vector3.Dot(offset, right));
        }

        private static (Vector3 Right, Vector3 Up) Basis(in Vector3 axis)
        {
            var reference = MathF.Abs(axis.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;
            var right = Vector3.Normalize(Vector3.Cross(reference, axis));

            return (right, Vector3.Cross(axis, right));
        }
    }
}
