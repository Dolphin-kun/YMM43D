using System.Numerics;

namespace YMM43D.PreviewTool
{
    internal sealed class ScreenRegion
    {
        private readonly Vector2[] outline;

        private ScreenRegion(SelectShape shape, Vector2[] outline, Vector2 center, float radius)
        {
            Shape = shape;
            this.outline = outline;
            Center = center;
            Radius = radius;
        }

        public SelectShape Shape { get; }

        public Vector2 Center { get; }

        public float Radius { get; }

        public IReadOnlyList<Vector2> Outline => outline;

        public static ScreenRegion Box(in Vector2 from, in Vector2 to)
        {
            var min = Vector2.Min(from, to);
            var max = Vector2.Max(from, to);

            return new ScreenRegion(SelectShape.Box, [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)], (min + max) / 2f, 0f);
        }

        public static ScreenRegion Lasso(IReadOnlyList<Vector2> points) => new(SelectShape.Lasso, [.. points], Vector2.Zero, 0f);

        public static ScreenRegion Circle(in Vector2 center, float radius) => new(SelectShape.Circle, [], center, radius);

        public bool IsUsable => Shape is SelectShape.Circle ? Radius > 0f : outline.Length >= 3 && Area() > 1f;

        public bool Contains(in Vector2 point)
        {
            if (Shape is SelectShape.Circle)
                return Vector2.DistanceSquared(point, Center) <= Radius * Radius;

            var inside = false;

            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                var a = outline[i];
                var b = outline[j];

                if ((a.Y > point.Y) != (b.Y > point.Y)
                    && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public bool Crosses(in Vector2 from, in Vector2 to)
        {
            if (Contains(from) || Contains(to))
                return true;

            if (Shape is SelectShape.Circle)
                return DistanceToSegment(Center, from, to) <= Radius;

            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                if (SegmentsCross(from, to, outline[j], outline[i]))
                    return true;
            }

            return false;
        }

        private float Area()
        {
            var sum = 0f;

            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
                sum += outline[j].X * outline[i].Y - outline[i].X * outline[j].Y;

            return MathF.Abs(sum) / 2f;
        }

        public static float DistanceToSegment(in Vector2 point, in Vector2 from, in Vector2 to)
        {
            var span = to - from;
            var lengthSquared = span.LengthSquared();

            if (lengthSquared < 1e-6f)
                return Vector2.Distance(point, from);

            var rate = Math.Clamp(Vector2.Dot(point - from, span) / lengthSquared, 0f, 1f);

            return Vector2.Distance(point, from + span * rate);
        }

        private static bool SegmentsCross(in Vector2 a, in Vector2 b, in Vector2 c, in Vector2 d)
        {
            static float Side(in Vector2 p, in Vector2 q, in Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);

            var first = Side(a, b, c);
            var second = Side(a, b, d);
            var third = Side(c, d, a);
            var fourth = Side(c, d, b);

            return first * second < 0f && third * fourth < 0f;
        }
    }
}
