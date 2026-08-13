using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct PickRay(Vector3 Origin, Vector3 Direction)
    {
        public static PickRay? FromScreen(
            Vector2 position, float width, float height, in Matrix4x4 viewProjection)
        {
            if (width <= 0f || height <= 0f || !Matrix4x4.Invert(viewProjection, out var inverse))
                return null;

            var ndc = new Vector2(
                position.X / width * 2f - 1f,
                1f - position.Y / height * 2f);

            var near = Unproject(new Vector3(ndc, 0f), inverse);
            var far = Unproject(new Vector3(ndc, 1f), inverse);

            if (near is not { } start || far is not { } end)
                return null;

            var direction = end - start;

            return direction.LengthSquared() > 0f
                ? new PickRay(start, Vector3.Normalize(direction))
                : null;
        }

        public float? IntersectUnitBox(in Matrix4x4 world, float minThickness = 0.001f)
            => IntersectBox(WorldBounds.FromCube(1f), world, minThickness);

        public float? IntersectBox(in WorldBounds bounds, in Matrix4x4 world, float minThickness = 0.001f)
        {
            if (!Matrix4x4.Invert(world, out var inverse))
                return null;

            var origin = Vector3.Transform(Origin, inverse);
            var direction = Vector3.TransformNormal(Direction, inverse);

            var center = (bounds.Min + bounds.Max) / 2f;
            var half = Vector3.Max((bounds.Max - bounds.Min) / 2f, new Vector3(minThickness / 2f));

            var enter = float.NegativeInfinity;
            var exit = float.PositiveInfinity;

            for (var axis = 0; axis < 3; axis++)
            {
                var slope = Component(direction, axis);
                var start = Component(origin, axis) - Component(center, axis);
                var limit = Component(half, axis);

                if (MathF.Abs(slope) < 1e-9f)
                {
                    if (MathF.Abs(start) > limit)
                        return null;

                    continue;
                }

                var first = (-limit - start) / slope;
                var second = (limit - start) / slope;

                if (first > second)
                    (first, second) = (second, first);

                enter = MathF.Max(enter, first);
                exit = MathF.Min(exit, second);

                if (enter > exit)
                    return null;
            }

            if (exit < 0f)
                return null;

            var local = MathF.Max(enter, 0f);

            var hit = Vector3.Transform(origin + direction * local, world);

            return Vector3.Distance(Origin, hit);
        }

        public Vector3? IntersectPlane(in Vector3 point, in Vector3 normal)
        {
            var slope = Vector3.Dot(Direction, normal);

            if (MathF.Abs(slope) < 1e-6f)
                return null;

            var distance = Vector3.Dot(point - Origin, normal) / slope;

            return distance > 0f ? Origin + Direction * distance : null;
        }

        private static Vector3? Unproject(in Vector3 ndc, in Matrix4x4 inverse)
        {
            var point = Vector4.Transform(new Vector4(ndc, 1f), inverse);

            if (MathF.Abs(point.W) < 1e-9f)
                return null;

            return new Vector3(point.X, point.Y, point.Z) / point.W;
        }

        private static float Component(in Vector3 value, int axis)
            => axis switch { 0 => value.X, 1 => value.Y, _ => value.Z };
    }
}
