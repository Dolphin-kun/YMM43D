using System.Numerics;

namespace YMM43D.Commons
{
    public enum GizmoHandle
    {
        None,

        MoveX,

        MoveY,

        MoveZ,

        RotateZ,

        Free,
    }

    public readonly record struct TransformGizmo(Vector3 Origin, float Scale)
    {
        public const float AxisLength = 1f;

        public const float RingRadius = 0.75f;

        public const int RingSegments = 48;

        public const float HeadLength = 0.18f;

        private const float ScreenRatio = 0.13f;

        public const float GrabThreshold = 9f;

        public static TransformGizmo Create(in Vector3 origin, in Vector3 cameraPosition)
        {
            var distance = Vector3.Distance(origin, cameraPosition);

            return new TransformGizmo(origin, MathF.Max(0.05f, distance * ScreenRatio));
        }

        public static Vector3 AxisDirection(GizmoHandle handle) => handle switch
        {
            GizmoHandle.MoveX => Vector3.UnitX,
            GizmoHandle.MoveY => Vector3.UnitY,
            GizmoHandle.MoveZ => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        public Vector3 AxisEnd(GizmoHandle handle)
            => Origin + AxisDirection(handle) * (AxisLength * Scale);

        public Vector3 RingPoint(int index)
        {
            var angle = MathF.Tau * index / RingSegments;

            return Origin + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * (RingRadius * Scale);
        }

        public static float? ClosestOnAxis(in PickRay ray, in Vector3 origin, in Vector3 axis)
        {
            var w = origin - ray.Origin;

            var axisDotRay = Vector3.Dot(axis, ray.Direction);
            var determinant = 1f - axisDotRay * axisDotRay;

            if (MathF.Abs(determinant) < 1e-6f)
                return null;

            var axisDotW = Vector3.Dot(axis, w);
            var rayDotW = Vector3.Dot(ray.Direction, w);

            return (axisDotRay * rayDotW - axisDotW) / determinant;
        }
    }
}
