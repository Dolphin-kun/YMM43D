using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public interface I3DGeometry : IDisposable
    {
        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer IndexBuffer { get; }
        public int IndexCount { get; }
        public InputElementDescription[] InputElements { get; }
    }
}
