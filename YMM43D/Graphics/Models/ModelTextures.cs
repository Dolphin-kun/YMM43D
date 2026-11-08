using Vortice.Direct3D11;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelTextures(
        ID3D11ShaderResourceView? Color,
        ID3D11ShaderResourceView? Bump,
        ID3D11ShaderResourceView? Glow,
        ID3D11ShaderResourceView? Feel,
        ID3D11ShaderResourceView? Shade);
}
