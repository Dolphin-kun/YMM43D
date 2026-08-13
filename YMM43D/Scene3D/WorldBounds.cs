using System.Numerics;

namespace YMM43D.Scene3D
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

        public Vector3[] GetCorners() =>
        [
            new(Min.X, Min.Y, Min.Z),
            new(Max.X, Min.Y, Min.Z),
            new(Min.X, Max.Y, Min.Z),
            new(Max.X, Max.Y, Min.Z),
            new(Min.X, Min.Y, Max.Z),
            new(Max.X, Min.Y, Max.Z),
            new(Min.X, Max.Y, Max.Z),
            new(Max.X, Max.Y, Max.Z),
        ];

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
