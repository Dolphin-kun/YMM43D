using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Meshes
{
    public sealed class SurfaceMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer => null;
        public int DrawCount { get; }
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public SurfaceMesh(ID3D11Device device, SurfaceGeometry geometry, IReadOnlyList<Color4> groupColors)
        {
            var vertices = new List<Vertex>();

            foreach (var face in geometry.Faces)
            {
                var indices = face.Indices;
                var color = Pick(groupColors, face.Group);

                for (var i = 1; i + 1 < indices.Length; i++)
                {
                    var corners = new[] { indices[0], indices[i], indices[i + 1] };

                    var a = geometry.Vertices[corners[0]];
                    var b = geometry.Vertices[corners[1]];
                    var c = geometry.Vertices[corners[2]];

                    var flat = -Vertex.GetNormal(a, b, c);

                    foreach (var corner in corners)
                    {
                        vertices.Add(new Vertex(
                            geometry.Vertices[corner],
                            color,
                            ToTexCoord(geometry.Vertices[corner]),
                            face.IsSmooth ? geometry.Normals[corner] : flat));
                    }
                }
            }

            DrawCount = vertices.Count;
            VertexBuffer = D3D11Buffers.Create(device, [.. vertices], BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
        }

        private static Vector2 ToTexCoord(in Vector3 position) => new(position.X + 0.5f, 0.5f - position.Y);

        private static Color4 Pick(IReadOnlyList<Color4> colors, int group)
            => colors.Count == 0 ? new Color4(1f, 1f, 1f, 1f) : colors[group % colors.Count];

        public void Dispose() => disposer.Dispose();
    }
}
