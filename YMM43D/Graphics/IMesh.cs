using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Direct3D;

namespace YMM43D.Graphics
{
    public interface IMesh : IDisposable
    {
        ID3D11Buffer VertexBuffer { get; }

        ID3D11Buffer? IndexBuffer { get; }

        int DrawCount { get; }

        int VertexStride { get; }

        InputElementDescription[] InputElements { get; }

        PrimitiveTopology Topology { get; }

        Format IndexFormat => Format.R16_UInt;
    }
}
