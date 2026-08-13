using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Meshes
{
    public sealed class BoxMesh : IMesh
    {
        private static readonly ushort[] Corners =
        [
            0, 1, 2,  1, 3, 2,
            5, 4, 7,  4, 6, 7,
            4, 0, 6,  0, 2, 6,
            1, 5, 3,  5, 7, 3,
            4, 5, 0,  5, 1, 0,
            2, 3, 6,  3, 7, 6,
        ];

        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer => null;
        public int DrawCount => Corners.Length;
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public static BoxMesh CreateUnitCube(ID3D11Device device) => new(device, -0.5f, 0.5f);

        public static BoxMesh CreateExtrusionBox(ID3D11Device device) => new(device, 0f, 1f);

        private BoxMesh(ID3D11Device device, float zNear, float zFar)
        {
            var white = new Color4(1f, 1f, 1f, 1f);

            Vector3[] positions =
            [
                new(-0.5f,  0.5f, zNear),
                new( 0.5f,  0.5f, zNear),
                new(-0.5f, -0.5f, zNear),
                new( 0.5f, -0.5f, zNear),
                new(-0.5f,  0.5f, zFar),
                new( 0.5f,  0.5f, zFar),
                new(-0.5f, -0.5f, zFar),
                new( 0.5f, -0.5f, zFar),
            ];

            var vertices = new Vertex[Corners.Length];

            for (var i = 0; i < Corners.Length; i += 3)
            {
                var a = positions[Corners[i]];
                var b = positions[Corners[i + 1]];
                var c = positions[Corners[i + 2]];

                var normal = Vertex.GetNormal(a, b, c);

                vertices[i] = new Vertex(a, white, ToTexCoord(a), normal);
                vertices[i + 1] = new Vertex(b, white, ToTexCoord(b), normal);
                vertices[i + 2] = new Vertex(c, white, ToTexCoord(c), normal);
            }

            VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
        }

        private static Vector2 ToTexCoord(in Vector3 position)
            => new(position.X + 0.5f, 0.5f - position.Y);

        public void Dispose() => disposer.Dispose();
    }
}
