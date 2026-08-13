using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public interface IMaterial : IDisposable
    {
        ID3D11VertexShader VertexShader { get; }

        ID3D11PixelShader PixelShader { get; }

        byte[] VertexShaderBytecode { get; }
    }
}
