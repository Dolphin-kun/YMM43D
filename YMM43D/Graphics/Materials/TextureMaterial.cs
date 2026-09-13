using Vortice.Direct3D11;

namespace YMM43D.Graphics.Materials
{
    public sealed class TextureMaterial(ID3D11Device device)
        : ShaderMaterial(device, typeof(TextureMaterial).Assembly, "TextureMaterial.hlsl");
}
