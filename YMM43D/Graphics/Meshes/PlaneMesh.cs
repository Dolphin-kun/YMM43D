using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Meshes
{
    /// <summary>
    /// 原点を中心とする、XY 平面上の 1×1 の板。
    /// テクスチャを貼って 2D アイテムを 3D 空間に配置するのに使います。
    /// </summary>
    public sealed class PlaneMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer { get; }
        public int DrawCount => 6;
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public PlaneMesh(ID3D11Device device)
        {
            var white = new Color4(1f, 1f, 1f, 1f);
            var vertices = new[]
            {
                new Vertex(new Vector3(-0.5f,  0.5f, 0f), white, new Vector2(0f, 0f)),
                new Vertex(new Vector3( 0.5f,  0.5f, 0f), white, new Vector2(1f, 0f)),
                new Vertex(new Vector3(-0.5f, -0.5f, 0f), white, new Vector2(0f, 1f)),
                new Vertex(new Vector3( 0.5f, -0.5f, 0f), white, new Vector2(1f, 1f)),
            };

            ushort[] indices = [0, 1, 2, 1, 3, 2];

            VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
            IndexBuffer = D3D11Buffers.Create(device, indices, BindFlags.IndexBuffer);
            disposer.Collect(IndexBuffer);
        }

        public void Dispose() => disposer.Dispose();
    }
}
