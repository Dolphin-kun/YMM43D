using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct ImageArea(Vector2 Min, Vector2 Max)
    {
        public static ImageArea Unbounded => new(new Vector2(float.NegativeInfinity), new Vector2(float.PositiveInfinity));

        public static ImageArea ForScreen(Vector2 screenSize, in ScreenPlacement placement, float margin)
        {
            if (!float.IsFinite(screenSize.X)
                || !float.IsFinite(screenSize.Y)
                || screenSize.X <= 0f
                || screenSize.Y <= 0f)
            {
                return Unbounded;
            }

            var half = screenSize / 2f * (1f + margin);
            var toImage = placement.ToImageSpace();

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            Span<Vector2> corners =
            [
                new(-half.X, -half.Y),
                new(half.X, -half.Y),
                new(half.X, half.Y),
                new(-half.X, half.Y),
            ];

            foreach (var corner in corners)
            {
                var image = Vector2.Transform(corner, toImage);

                if (!float.IsFinite(image.X) || !float.IsFinite(image.Y))
                    return Unbounded;

                min = Vector2.Min(min, image);
                max = Vector2.Max(max, image);
            }

            return new ImageArea(min, max);
        }
    }
}
