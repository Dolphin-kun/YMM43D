using System.Numerics;

namespace YMM43D.Commons
{
    public enum GizmoHandle
    {
        None,

        MoveX,

        MoveY,

        MoveZ,

        RotateX,

        RotateY,

        RotateZ,

        Free,
    }

    // つまみの出し方。動かす矢印を出すか、回す輪を出すか。
    public enum GizmoStyle
    {
        // 矢印だけ（マーカー）
        Move,

        // 矢印と、画面に沿って回す輪（アイテム）
        MoveAndTurn,

        // 3 本の軸まわりに回す輪だけ（骨）
        Turn,
    }

    public readonly record struct TransformGizmo(Vector3 Origin, float Scale, Matrix4x4 Orientation)
    {
        public const float AxisLength = 1f;

        public const float RingRadius = 0.75f;

        public const int RingSegments = 48;

        public const float HeadLength = 0.18f;

        private const float ScreenRatio = 0.13f;

        public const float GrabThreshold = 9f;

        // 骨のつまみはアイテムのものより小さく出す。体の中に埋もれない程度の大きさ。
        public const float BoneRatio = 0.4f;

        public static TransformGizmo Create(
            in Vector3 origin, in Vector3 cameraPosition, in Matrix4x4? orientation = null, float ratio = 1f)
        {
            var distance = Vector3.Distance(origin, cameraPosition);

            return new TransformGizmo(
                origin, MathF.Max(0.05f, distance * ScreenRatio * ratio), orientation ?? Matrix4x4.Identity);
        }

        public static Vector3 AxisDirection(GizmoHandle handle) => handle switch
        {
            GizmoHandle.MoveX => Vector3.UnitX,
            GizmoHandle.MoveY => Vector3.UnitY,
            GizmoHandle.MoveZ => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        // 傾けて出しているときの、その軸が向いている先。
        public Vector3 Axis(GizmoHandle handle)
        {
            var axis = Vector3.TransformNormal(AxisDirection(handle), Orientation);

            return axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : AxisDirection(handle);
        }

        public Vector3 AxisEnd(GizmoHandle handle)
            => Origin + Axis(handle) * (AxisLength * Scale);

        public static Vector3 TurnDirection(GizmoHandle handle) => handle switch
        {
            GizmoHandle.RotateX => Vector3.UnitX,
            GizmoHandle.RotateY => Vector3.UnitY,
            GizmoHandle.RotateZ => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        // その輪を掴んだときに、まわりを回ることになる軸。
        public Vector3 TurnAxis(GizmoHandle handle)
        {
            var axis = Vector3.TransformNormal(TurnDirection(handle), Orientation);

            return axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : TurnDirection(handle);
        }

        // 回す輪の 1 点。輪は、その軸に垂直な面の上に描く。
        public static Vector3 RingOffset(GizmoHandle handle, int index)
        {
            var angle = MathF.Tau * index / RingSegments;
            var (cos, sin) = (MathF.Cos(angle) * RingRadius, MathF.Sin(angle) * RingRadius);

            return handle switch
            {
                GizmoHandle.RotateX => new Vector3(0f, cos, sin),
                GizmoHandle.RotateY => new Vector3(sin, 0f, cos),
                _ => new Vector3(cos, sin, 0f),
            };
        }

        public Vector3 RingPoint(GizmoHandle handle, int index)
            => Origin + Vector3.TransformNormal(RingOffset(handle, index) * Scale, Orientation);


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
