using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public interface I3DTextureProvider
    {
        ID3D11ShaderResourceView? GetTexture(ID3D11Device device);
    }
}
