using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public interface I3DMaterial : IDisposable
    {
        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }
    }
}
