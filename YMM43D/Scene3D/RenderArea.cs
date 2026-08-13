using System.Numerics;

namespace YMM43D.Scene3D
{
    public readonly record struct RenderArea(int Width, int Height, Vector2 Origin)
    {
        private const float MaxTangent = 64f;

        private static readonly (int From, int To)[] Edges =
        [
            (0, 1), (2, 3), (4, 5), (6, 7),
            (0, 2), (1, 3), (4, 6), (5, 7),
            (0, 4), (1, 5), (2, 6), (3, 7),
        ];

        public static RenderArea? Measure(
            in WorldBounds bounds,
            in Matrix4x4 world,
            in Matrix4x4 view,
            in Matrix3x2 tangentToImage,
            in ImageArea visible,
            float nearDistance,
            int maxSize)
        {
            if (bounds.IsEmpty || maxSize <= 0)
                return null;

            var worldView = world * view;
            var corners = bounds.GetCorners();

            for (var i = 0; i < corners.Length; i++)
                corners[i] = Vector3.Transform(corners[i], worldView);

            var visiblePoints = new List<Vector3>(corners.Length + Edges.Length);

            foreach (var corner in corners)
                if (-corner.Z >= nearDistance)
                    visiblePoints.Add(corner);

            foreach (var (from, to) in Edges)
                if (CrossNear(corners[from], corners[to], nearDistance) is { } crossing)
                    visiblePoints.Add(crossing);

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            var found = false;

            foreach (var point in visiblePoints)
            {
                var depth = MathF.Max(-point.Z, nearDistance);
                var tangent = new Vector2(point.X / depth, point.Y / depth);

                if (!float.IsFinite(tangent.X) || !float.IsFinite(tangent.Y))
                    continue;

                tangent = Vector2.Clamp(tangent, new Vector2(-MaxTangent), new Vector2(MaxTangent));

                var image = Vector2.Transform(tangent, tangentToImage);

                if (!float.IsFinite(image.X) || !float.IsFinite(image.Y))
                    continue;

                min = Vector2.Min(min, image);
                max = Vector2.Max(max, image);
                found = true;
            }

            if (!found)
                return null;

            min = Vector2.Max(min, visible.Min);
            max = Vector2.Min(max, visible.Max);

            var width = (int)MathF.Ceiling(max.X - min.X);
            var height = (int)MathF.Ceiling(max.Y - min.Y);

            if (width <= 0 || height <= 0)
                return null;

            if (width > maxSize)
            {
                min.X = Anchor(min.X, max.X, visible.Min.X, visible.Max.X, maxSize);
                width = maxSize;
            }

            if (height > maxSize)
            {
                min.Y = Anchor(min.Y, max.Y, visible.Min.Y, visible.Max.Y, maxSize);
                height = maxSize;
            }

            return new RenderArea(width, height, min);
        }

        private static float Anchor(float min, float max, float visibleMin, float visibleMax, int size)
        {
            var centre = (min + max) / 2f;

            if (float.IsFinite(visibleMin) && float.IsFinite(visibleMax))
                centre = (visibleMin + visibleMax) / 2f;

            return centre - size / 2f;
        }

        private static Vector3? CrossNear(in Vector3 from, in Vector3 to, float nearDistance)
        {
            var start = -from.Z - nearDistance;
            var end = -to.Z - nearDistance;

            if (start == end || (start < 0f) == (end < 0f))
                return null;

            return Vector3.Lerp(from, to, start / (start - end));
        }
    }

    public readonly record struct ImageArea(Vector2 Min, Vector2 Max)
    {
        public static ImageArea Unbounded
            => new(new Vector2(float.NegativeInfinity), new Vector2(float.PositiveInfinity));

        public static ImageArea ForScreen(Vector2 screenSize, in ScreenPlacement placement, float margin)
        {
            if (!float.IsFinite(screenSize.X) || !float.IsFinite(screenSize.Y)
                || screenSize.X <= 0f || screenSize.Y <= 0f)
            {
                return Unbounded;
            }

            var half = screenSize / 2f * (1f + margin);
            var toImage = placement.ToImageSpace();

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var corner in Corners(half))
            {
                var image = Vector2.Transform(corner, toImage);

                if (!float.IsFinite(image.X) || !float.IsFinite(image.Y))
                    return Unbounded;

                min = Vector2.Min(min, image);
                max = Vector2.Max(max, image);
            }

            return new ImageArea(min, max);
        }

        private static Vector2[] Corners(Vector2 half) =>
        [
            new(-half.X, -half.Y),
            new(half.X, -half.Y),
            new(half.X, half.Y),
            new(-half.X, half.Y),
        ];
    }
}
