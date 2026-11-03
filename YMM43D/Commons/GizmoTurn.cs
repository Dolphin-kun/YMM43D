using System.Numerics;

namespace YMM43D.Commons
{
    public static class GizmoTurn
    {
        public static Matrix4x4 Apply(
            in Matrix4x4 start,
            in Matrix4x4 frameInverse,
            in Vector3 worldAxis,
            GizmoHandle handle,
            float degrees,
            GizmoSpace space)
        {
            var radians = float.DegreesToRadians(degrees);

            if (space is GizmoSpace.Local)
            {
                return Matrix4x4.CreateFromAxisAngle(TransformGizmo.TurnDirection(handle), radians) * start;
            }

            var turned = Vector3.TransformNormal(worldAxis, frameInverse);
            var axis = turned.LengthSquared() > 1e-12f ? Vector3.Normalize(turned) : worldAxis;

            return start * Matrix4x4.CreateFromAxisAngle(axis, radians);
        }

        public static float Measure(in PickRay ray, in Vector3 origin, in Vector3 axis, in Vector3 right, in Vector3 up)
        {
            if (ray.IntersectPlane(origin, axis) is not { } hit)
                return float.NaN;

            var offset = hit - origin;

            return MathF.Atan2(Vector3.Dot(offset, up), Vector3.Dot(offset, right));
        }
    }
}
