using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Meshes;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Plugin
{
    public sealed class DeformSolidMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }

        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount { get; }

        public int VertexStride => DeformSolidVertex.Stride;

        public InputElementDescription[] InputElements => DeformSolidVertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public Matrix4x4 Fit { get; }

        public DeformSolidMesh(ID3D11Device device, SurfaceGeometry geometry, IReadOnlyList<Color4> groupColors, in DeformGrid grid)
        {
            var (center, span) = Measure(geometry.Vertices);

            Fit = Matrix4x4.CreateScale(span) * Matrix4x4.CreateTranslation(center);

            var cells = new Vector3(Math.Max(grid.X, 1), Math.Max(grid.Y, 1), Math.Max(Math.Min(grid.X, grid.Y), 1));

            var vertices = new List<DeformSolidVertex>();

            foreach (var face in geometry.Faces)
            {
                var indices = face.Indices;
                var color = Pick(groupColors, face.Group);

                for (var i = 1; i + 1 < indices.Length; i++)
                {
                    ReadOnlySpan<int> corners = [indices[0], indices[i], indices[i + 1]];

                    var first = Fitted(geometry.Vertices[corners[0]], center, span);
                    var second = Fitted(geometry.Vertices[corners[1]], center, span);
                    var third = Fitted(geometry.Vertices[corners[2]], center, span);

                    var flat = -Vertex.GetNormal(first, second, third);
                    var piece = Cell((first + second + third) / 3f, cells);

                    Add(vertices, first, color, face.IsSmooth ? Normal(geometry, corners[0], flat) : flat, piece);
                    Add(vertices, second, color, face.IsSmooth ? Normal(geometry, corners[1], flat) : flat, piece);
                    Add(vertices, third, color, face.IsSmooth ? Normal(geometry, corners[2], flat) : flat, piece);
                }
            }

            DrawCount = vertices.Count;
            VertexBuffer = disposer.CollectEx(D3D11Buffers.Create(device, [.. vertices], BindFlags.VertexBuffer));
        }

        public static Vector3 Cell(in Vector3 position, in Vector3 cells)
        {
            var at = position + new Vector3(0.5f);

            var picked = new Vector3(
                MathF.Floor(Math.Clamp(at.X, 0f, 0.999999f) * cells.X),
                MathF.Floor(Math.Clamp(at.Y, 0f, 0.999999f) * cells.Y),
                MathF.Floor(Math.Clamp(at.Z, 0f, 0.999999f) * cells.Z));

            return new Vector3(
                (picked.X + 0.5f) / cells.X - 0.5f,
                (picked.Y + 0.5f) / cells.Y - 0.5f,
                (picked.Z + 0.5f) / cells.Z - 0.5f);
        }

        public static (Vector3 Center, float Span) Measure(IReadOnlyList<Vector3> points)
        {
            if (points.Count == 0)
                return (Vector3.Zero, 1f);

            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);

            foreach (var point in points)
            {
                low = Vector3.Min(low, point);
                high = Vector3.Max(high, point);
            }

            var extent = high - low;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));

            return ((low + high) * 0.5f, longest > 1e-6f ? longest : 1f);
        }

        private static Vector3 Fitted(in Vector3 position, in Vector3 center, float span) => (position - center) / span;

        private static Vector3 Normal(SurfaceGeometry geometry, int corner, in Vector3 flat)
        {
            var found = corner < geometry.Normals.Length ? geometry.Normals[corner] : Vector3.Zero;

            return found.LengthSquared() > 1e-10f ? Vector3.Normalize(found) : flat;
        }

        private static void Add(List<DeformSolidVertex> into, in Vector3 position, in Color4 color, in Vector3 normal, in Vector3 piece)
            => into.Add(new DeformSolidVertex(position, color, normal, piece));

        private static Color4 Pick(IReadOnlyList<Color4> colors, int group)
            => colors.Count == 0 ? new Color4(1f, 1f, 1f, 1f) : colors[Math.Abs(group) % colors.Count];

        public void Dispose() => disposer.Dispose();
    }
}
