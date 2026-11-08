using System.Numerics;

namespace YMM43D.Commons
{
    public enum GizmoHandle
    {
        None,

        MoveX,

        MoveY,

        MoveZ,

        PlaneXY,

        PlaneYZ,

        PlaneZX,

        RotateX,

        RotateY,

        RotateZ,

        ScaleX,

        ScaleY,

        ScaleZ,

        ScaleAll,

        Inflate,

        Free,
    }

    public enum GizmoStyle
    {
        Move,

        Turn,

        Scale,
    }

    public enum GizmoMode
    {
        Move,

        Rotate,

        Scale,

        Select,

        LoopCut,

        Inflate,
    }

    public enum GizmoSpace
    {
        World,

        Local,
    }

    public readonly record struct TransformGizmo(Vector3 Origin, float Scale, Matrix4x4 Orientation)
    {
        public const float AxisLength = 1f;

        public const float RingRadius = 0.75f;

        public const int RingSegments = 48;

        public const float HeadLength = 0.18f;

        public const float ScaleLength = 0.82f;

        public const float ScaleBox = 0.09f;

        public const float PlaneNear = 0.26f;

        public const float PlaneFar = 0.5f;

        public const int PlaneCorners = 4;

        private const float ScreenRatio = 0.18f;

        public const float GrabThreshold = 9f;

        public const float CenterGrab = 11f;

        public const float CenterReach = 2.4f;

        public const float BoneRatio = 0.6f;

        public static TransformGizmo Create(
            in Vector3 origin, in Vector3 cameraPosition, in Matrix4x4? orientation = null, float ratio = 1f)
            => AtDistance(origin, Vector3.Distance(origin, cameraPosition), orientation, ratio);

        public static TransformGizmo AtDistance(
            in Vector3 origin, float distance, in Matrix4x4? orientation = null, float ratio = 1f)
            => new(origin, MathF.Max(0.05f, distance * ScreenRatio * ratio), orientation ?? Matrix4x4.Identity);

        public static bool IsAxis(GizmoHandle handle)
            => handle is GizmoHandle.MoveX or GizmoHandle.MoveY or GizmoHandle.MoveZ;

        public static bool IsPlane(GizmoHandle handle)
            => handle is GizmoHandle.PlaneXY or GizmoHandle.PlaneYZ or GizmoHandle.PlaneZX;

        public static bool IsTurn(GizmoHandle handle)
            => handle is GizmoHandle.RotateX or GizmoHandle.RotateY or GizmoHandle.RotateZ;

        public static bool IsScale(GizmoHandle handle)
            => handle is GizmoHandle.ScaleX or GizmoHandle.ScaleY or GizmoHandle.ScaleZ;

        public static bool ShowsHandles(GizmoMode mode)
            => mode is not (GizmoMode.Select or GizmoMode.LoopCut or GizmoMode.Inflate);

        public static GizmoStyle StyleOf(GizmoMode mode) => mode switch
        {
            GizmoMode.Rotate => GizmoStyle.Turn,
            GizmoMode.Scale => GizmoStyle.Scale,
            _ => GizmoStyle.Move,
        };

        public static Vector3 AxisDirection(GizmoHandle handle) => handle switch
        {
            GizmoHandle.MoveX or GizmoHandle.ScaleX => Vector3.UnitX,
            GizmoHandle.MoveY or GizmoHandle.ScaleY => Vector3.UnitY,
            GizmoHandle.MoveZ or GizmoHandle.ScaleZ => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        public Vector3 Axis(GizmoHandle handle) => Along(AxisDirection(handle));

        public Vector3 AxisEnd(GizmoHandle handle)
            => Origin + Axis(handle) * (AxisLength * Scale);

        public Vector3 ScaleEnd(GizmoHandle handle)
            => Origin + Axis(handle) * (ScaleLength * Scale);

        public static Vector3 PlaneNormalDirection(GizmoHandle handle) => handle switch
        {
            GizmoHandle.PlaneXY => Vector3.UnitZ,
            GizmoHandle.PlaneYZ => Vector3.UnitX,
            GizmoHandle.PlaneZX => Vector3.UnitY,
            _ => Vector3.Zero,
        };

        public Vector3 PlaneNormal(GizmoHandle handle) => Along(PlaneNormalDirection(handle));

        public static Vector3 PlaneCorner(GizmoHandle handle, int index)
        {
            var (first, second) = (index % PlaneCorners) switch
            {
                0 => (PlaneNear, PlaneNear),
                1 => (PlaneFar, PlaneNear),
                2 => (PlaneFar, PlaneFar),
                _ => (PlaneNear, PlaneFar),
            };

            return handle switch
            {
                GizmoHandle.PlaneXY => new Vector3(first, second, 0f),
                GizmoHandle.PlaneYZ => new Vector3(0f, first, second),
                GizmoHandle.PlaneZX => new Vector3(second, 0f, first),
                _ => Vector3.Zero,
            };
        }

        public Vector3 PlanePoint(GizmoHandle handle, int index)
            => Origin + Vector3.TransformNormal(PlaneCorner(handle, index) * Scale, Orientation);

        public static Vector3 TurnDirection(GizmoHandle handle) => handle switch
        {
            GizmoHandle.RotateX => Vector3.UnitX,
            GizmoHandle.RotateY => Vector3.UnitY,
            GizmoHandle.RotateZ => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        public Vector3 TurnAxis(GizmoHandle handle) => Along(TurnDirection(handle));

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

        private Vector3 Along(in Vector3 direction)
        {
            if (direction == Vector3.Zero)
                return direction;

            var turned = Vector3.TransformNormal(direction, Orientation);

            return turned.LengthSquared() > 1e-12f ? Vector3.Normalize(turned) : direction;
        }

        public static (Vector3 Right, Vector3 Up) PlaneBasis(in Vector3 axis)
        {
            var guess = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Cross(guess, axis);

            right = right.LengthSquared() > 1e-12f ? Vector3.Normalize(right) : Vector3.UnitX;

            return (right, Vector3.Cross(axis, right));
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
