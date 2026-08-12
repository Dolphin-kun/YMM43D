using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Meshes
{
    /// <summary>
    /// Y=0 に広がる巨大な地面。グリッド線はシェーダー側で描くため、
    /// 形状としては四隅の4頂点だけを持ちます。
    /// </summary>
    public sealed class GroundPlaneMesh : IMesh
    {
        private const float HalfSize = 1000f;

        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }

        /// <summary>四隅を <see cref="PrimitiveTopology.TriangleStrip"/> で描くためインデックスは使いません。</summary>
        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount => 4;
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleStrip;

        public GroundPlaneMesh(ID3D11Device device)
        {
            var white = new Color4(1f, 1f, 1f, 1f);
            var vertices = new[]
            {
                new Vertex(new Vector3(-HalfSize, 0f,  HalfSize), white, new Vector2(0f, 0f)),
                new Vertex(new Vector3( HalfSize, 0f,  HalfSize), white, new Vector2(1f, 0f)),
                new Vertex(new Vector3(-HalfSize, 0f, -HalfSize), white, new Vector2(0f, 1f)),
                new Vertex(new Vector3( HalfSize, 0f, -HalfSize), white, new Vector2(1f, 1f)),
            };

            VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
        }

        public void Dispose() => disposer.Dispose();
    }
}
