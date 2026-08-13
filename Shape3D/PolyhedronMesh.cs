using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;
using Color = System.Windows.Media.Color;

namespace Shape3D
{
    internal sealed class PolyhedronMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public SolidKind Kind { get; }
        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer => null;
        public int DrawCount { get; }
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public PolyhedronMesh(ID3D11Device device, SolidKind kind, IReadOnlyList<Color> faceColors)
        {
            Kind = kind;

            var solid = Polyhedron.Get(kind);
            var vertices = new List<Vertex>();

            for (var face = 0; face < solid.Faces.Length; face++)
            {
                var indices = solid.Faces[face];
                var color = ToColor4(faceColors, face);

                for (var i = 1; i + 1 < indices.Length; i++)
                {
                    vertices.Add(new Vertex(solid.Vertices[indices[0]], color, new Vector2(0.5f, 0f)));
                    vertices.Add(new Vertex(solid.Vertices[indices[i]], color, new Vector2(1f, 1f)));
                    vertices.Add(new Vertex(solid.Vertices[indices[i + 1]], color, new Vector2(0f, 1f)));
                }
            }

            DrawCount = vertices.Count;
            VertexBuffer = D3D11Buffers.Create(device, [.. vertices], BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
        }

        private static Color4 ToColor4(IReadOnlyList<Color> colors, int face)
        {
            if (colors.Count == 0)
                return new Color4(1f, 1f, 1f, 1f);

            var color = colors[face % colors.Count];

            return new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }

        public void Dispose() => disposer.Dispose();
    }
}
