using System.Numerics;
using YMM43D.Commons;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool
{
    internal sealed class BoneDragController
    {
        private ModelBonePose? pose;
        private EditScope scope;
        private GizmoHandle handle;

        private Vector3 origin;
        private Vector3 axis;
        private Vector3 right;
        private Vector3 up;

        private float lastAngle;
        private float rawTurn;
        private float appliedTurn;

        private Animation? turnX;
        private Animation? turnY;
        private Animation? turnZ;
        private Matrix4x4 frame;
        private Matrix4x4 inverseFrame;
        private Matrix4x4 startRotation;
        private Vector3 appliedAngles;

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

            axis = gizmo.TurnAxis(grabbed);

            if (axis.LengthSquared() < 1e-12f)
                return false;

            origin = gizmo.Origin;
            (right, up) = Basis(axis);

            if (GetAngle(ray) is not { } angle || !Prepare(bone, edit))
                return false;

            handle = grabbed;
            lastAngle = angle;
            rawTurn = 0f;
            appliedTurn = 0f;

            return true;
        }

        private bool Prepare(in PreviewBone bone, in EditScope edit)
        {
            frame = Orientation(bone.Frame);

            if (!Matrix4x4.Invert(frame, out inverseFrame))
                return false;

            var found = bone.Parameter.BoneFor(bone.Name);

            turnX = found.RotationX;
            turnY = found.RotationY;
            turnZ = found.RotationZ;

            startRotation = Rotation3D.ForObject(
                turnX.GetFloat(bone.Time), turnY.GetFloat(bone.Time), turnZ.GetFloat(bone.Time));

            appliedAngles = Rotation3D.ToAngles(startRotation);

            pose = found;
            scope = edit;

            return true;
        }

        public bool Update(in PickRay ray, in SnapGrid snap)
        {
            if (pose is null || GetAngle(ray) is not { } angle)
                return false;

            var delta = angle - lastAngle;
            delta -= MathF.Tau * MathF.Round(delta / MathF.Tau);
            lastAngle = angle;

            rawTurn += float.RadiansToDegrees(delta);

            var wanted = snap.SnapTurn(0f, rawTurn);

            if (wanted == appliedTurn)
                return false;

            appliedTurn = wanted;

            return Apply(Matrix4x4.CreateFromAxisAngle(axis, float.DegreesToRadians(wanted)));
        }

        private bool Apply(in Matrix4x4 turn)
        {
            if (turnX is null || turnY is null || turnZ is null)
                return false;

            var angles = Rotation3D.ToAngles(startRotation * frame * turn * inverseFrame);

            var change = new Vector3(
                Rotation3D.Wrap(angles.X - appliedAngles.X),
                Rotation3D.Wrap(angles.Y - appliedAngles.Y),
                Rotation3D.Wrap(angles.Z - appliedAngles.Z));

            if (MathF.Abs(change.X) < 1e-4f && MathF.Abs(change.Y) < 1e-4f && MathF.Abs(change.Z) < 1e-4f)
                return false;

            appliedAngles = angles;

            scope.Nudge(turnX, change.X);
            scope.Nudge(turnY, change.Y);
            scope.Nudge(turnZ, change.Z);

            return true;
        }

        private static Matrix4x4 Orientation(in Matrix4x4 matrix)
        {
            var made = Matrix4x4.Identity;

            var x = Row(matrix, 0);
            var y = Row(matrix, 1);
            var z = Row(matrix, 2);

            made.M11 = x.X; made.M12 = x.Y; made.M13 = x.Z;
            made.M21 = y.X; made.M22 = y.Y; made.M23 = y.Z;
            made.M31 = z.X; made.M32 = z.Y; made.M33 = z.Z;

            return made;
        }

        private static Vector3 Row(in Matrix4x4 matrix, int row)
        {
            var value = row switch
            {
                0 => new Vector3(matrix.M11, matrix.M12, matrix.M13),
                1 => new Vector3(matrix.M21, matrix.M22, matrix.M23),
                _ => new Vector3(matrix.M31, matrix.M32, matrix.M33),
            };

            return value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : value;
        }

        public void End()
        {
            pose = null;
            turnX = null;
            turnY = null;
            turnZ = null;
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
