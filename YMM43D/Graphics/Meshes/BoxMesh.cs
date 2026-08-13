using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Meshes
{
    public sealed class BoxMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer { get; }
        public int DrawCount => 36;
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public static BoxMesh CreateUnitCube(ID3D11Device device) => new(device, -0.5f, 0.5f);

        public static BoxMesh CreateExtrusionBox(ID3D11Device device) => new(device, 0f, 1f);

        private BoxMesh(ID3D11Device device, float zNear, float zFar)
        {
            var white = new Color4(1f, 1f, 1f, 1f);

            var vertices = new[]
            {
                new Vertex(new Vector3(-0.5f,  0.5f, zNear), white, new Vector2(0f, 0f)),
                new Vertex(new Vector3( 0.5f,  0.5f, zNear), white, new Vector2(1f, 0f)),
                new Vertex(new Vector3(-0.5f, -0.5f, zNear), white, new Vector2(0f, 1f)),
                new Vertex(new Vector3( 0.5f, -0.5f, zNear), white, new Vector2(1f, 1f)),
                new Vertex(new Vector3(-0.5f,  0.5f, zFar),  white, new Vector2(0f, 0f)),
                new Vertex(new Vector3( 0.5f,  0.5f, zFar),  white, new Vector2(1f, 0f)),
                new Vertex(new Vector3(-0.5f, -0.5f, zFar),  white, new Vector2(0f, 1f)),
                new Vertex(new Vector3( 0.5f, -0.5f, zFar),  white, new Vector2(1f, 1f)),
            };

            ushort[] indices =
            [
                0, 1, 2,  1, 3, 2,
                5, 4, 7,  4, 6, 7,
                4, 0, 6,  0, 2, 6,
                1, 5, 3,  5, 7, 3,
                4, 5, 0,  5, 1, 0,
                2, 3, 6,  3, 7, 6,
            ];

            VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
            IndexBuffer = D3D11Buffers.Create(device, indices, BindFlags.IndexBuffer);
            disposer.Collect(IndexBuffer);
        }

        public void Dispose() => disposer.Dispose();
    }
}
