using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class FillMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public FillMesh(ID3D11Device device, ReadOnlySpan<Vector3> corners, Color4 color)
        {
            var vertices = new Vertex[corners.Length];

            for (var i = 0; i < corners.Length; i++)
                vertices[i] = new Vertex(corners[i], color, Vector2.Zero);

            DrawCount = vertices.Length;
            VertexBuffer = disposer.CollectEx(D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer));
        }

        public ID3D11Buffer VertexBuffer { get; }

        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount { get; }

        public int VertexStride => Vertex.Stride;

        public InputElementDescription[] InputElements => Vertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public void Dispose() => disposer.Dispose();
    }
}
