using System.Numerics;

namespace YMM43D.Commons
{
    public static class Stretch3D
    {
        public const float MinReach = 1e-4f;

        public static Matrix4x4 Matrix(float uniform, float percentX, float percentY, float percentZ)
            => Matrix4x4.CreateScale(
                uniform * percentX / 100f,
                uniform * percentY / 100f,
                uniform * percentZ / 100f);

        public static float CenterSpan(float gizmoScale)
            => MathF.Max(gizmoScale * TransformGizmo.ScaleLength * TransformGizmo.CenterReach, MinReach);

        public static float CenterRatio(float reach, float start, float span)
            => MathF.Max(1f + (reach - start) / MathF.Max(span, MinReach), 0f);

        public static bool TryReach(in PickRay ray, in Vector3 origin, in Vector3 axis, out float reach)
        {
            reach = 0f;

            if (TransformGizmo.ClosestOnAxis(ray, origin, axis) is not { } position
                || MathF.Abs(position) < MinReach)
            {
                return false;
            }

            reach = position;

            return true;
        }

        public static float Ratio(in PickRay ray, in Vector3 origin, in Vector3 axis, float reach)
        {
            if (MathF.Abs(reach) < MinReach
                || TransformGizmo.ClosestOnAxis(ray, origin, axis) is not { } position)
            {
                return float.NaN;
            }

            return MathF.Max(position / reach, 0f);
        }
    }
}
