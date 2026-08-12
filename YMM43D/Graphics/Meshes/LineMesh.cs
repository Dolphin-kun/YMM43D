using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Meshes
{
    /// <summary>
    /// 線分の集まり。ワイヤーフレームのガイド表示に使います。
    /// </summary>
    public sealed class LineMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer => null;
        public int DrawCount { get; }
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.LineList;

        /// <param name="points">
        /// 線分の端点。2個で1本の線分になるため、要素数は偶数である必要があります。
        /// </param>
        public LineMesh(ID3D11Device device, ReadOnlySpan<Vector3> points, Color4 color)
        {
            if (points.Length == 0 || points.Length % 2 != 0)
                throw new ArgumentException("線分の端点は2個1組で指定してください。", nameof(points));

            var vertices = new Vertex[points.Length];
            for (var i = 0; i < points.Length; i++)
                vertices[i] = new Vertex(points[i], color, Vector2.Zero);

            DrawCount = vertices.Length;
            VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
        }

        public void Dispose() => disposer.Dispose();
    }
}
