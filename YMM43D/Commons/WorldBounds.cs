using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct WorldBounds(Vector3 Min, Vector3 Max)
    {
        public static WorldBounds Empty => new(Vector3.Zero, Vector3.Zero);

        public static WorldBounds FromCube(float edgeLength)
        {
            var half = new Vector3(edgeLength / 2f);
            return new WorldBounds(-half, half);
        }

        public static WorldBounds FromPoints(ReadOnlySpan<Vector3> points, in Matrix4x4 transform)
        {
            if (points.IsEmpty)
                return Empty;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var point in points)
            {
                var moved = Vector3.Transform(point, transform);
                min = Vector3.Min(min, moved);
                max = Vector3.Max(max, moved);
            }

            return new WorldBounds(min, max);
        }

        public bool IsEmpty => Max.X <= Min.X || Max.Y <= Min.Y;

        public Vector3 Center => (Min + Max) / 2f;

        public const int CornerCount = 8;

        public Vector3[] GetCorners()
        {
            var corners = new Vector3[CornerCount];

            WriteCorners(corners);

            return corners;
        }

        public void WriteCorners(Span<Vector3> into)
        {
            into[0] = new Vector3(Min.X, Min.Y, Min.Z);
            into[1] = new Vector3(Max.X, Min.Y, Min.Z);
            into[2] = new Vector3(Min.X, Max.Y, Min.Z);
            into[3] = new Vector3(Max.X, Max.Y, Min.Z);
            into[4] = new Vector3(Min.X, Min.Y, Max.Z);
            into[5] = new Vector3(Max.X, Min.Y, Max.Z);
            into[6] = new Vector3(Min.X, Max.Y, Max.Z);
            into[7] = new Vector3(Max.X, Max.Y, Max.Z);
        }

        public WorldBounds Transform(in Matrix4x4 matrix)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var corner in GetCorners())
            {
                var moved = Vector3.Transform(corner, matrix);
                min = Vector3.Min(min, moved);
                max = Vector3.Max(max, moved);
            }

            return new WorldBounds(min, max);
        }
    }
}
