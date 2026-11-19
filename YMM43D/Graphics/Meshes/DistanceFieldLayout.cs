using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public readonly record struct DistanceFieldLayout(
        Vector3 Center,
        Vector3 Scale,
        Vector3 Size,
        float CellSize,
        int CellsX,
        int CellsY,
        int CellsZ)
    {
        public const int LeastResolution = 16;
        public const int MostResolution = 192;
        private const int Padding = 3;

        public Matrix4x4 BoxMatrix => Matrix4x4.CreateScale(Size / Scale) * Matrix4x4.CreateTranslation(Center);

        public long CellCount => (long)CellsX * CellsY * CellsZ;

        public static DistanceFieldLayout? Measure(IReadOnlyList<Vector3> vertices, Vector3 scale, int resolution)
        {
            if (vertices.Count == 0)
                return null;

            scale = Vector3.Max(Vector3.Abs(scale), new Vector3(1e-6f));

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var vertex in vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }

            var extent = (max - min) * scale;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));

            if (!float.IsFinite(longest) || longest <= 0f)
                return null;

            var cellSize = longest / Math.Clamp(resolution, LeastResolution, MostResolution);

            var x = Cells(extent.X, cellSize);
            var y = Cells(extent.Y, cellSize);
            var z = Cells(extent.Z, cellSize);

            return new DistanceFieldLayout(
                (min + max) / 2f, scale, new Vector3(x, y, z) * cellSize, cellSize, x, y, z);
        }

        private static int Cells(float extent, float cellSize) => (int)MathF.Ceiling(extent / cellSize) + Padding * 2;
    }
}
