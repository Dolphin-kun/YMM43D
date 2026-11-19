using Vortice.Direct3D;
using Vortice.Direct3D11;
using YMM43D.Graphics;

namespace Reflection3D
{
    internal sealed class ReflectorMesh(ID3D11Device device, ReflectorShape shape) : IMesh
    {
        public ReflectorShape Shape { get; } = shape;

        public ID3D11Buffer VertexBuffer { get; } = D3D11Buffers.Create(device, shape.Vertices, BindFlags.VertexBuffer);

        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount => Shape.Vertices.Length;

        public int VertexStride => ReflectorVertex.Stride;

        public InputElementDescription[] InputElements => ReflectorVertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public void Dispose() => VertexBuffer.Dispose();
    }
}
