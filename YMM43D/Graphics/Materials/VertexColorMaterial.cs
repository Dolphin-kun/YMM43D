using Vortice.Direct3D11;

namespace YMM43D.Graphics.Materials
{
    public sealed class VertexColorMaterial(ID3D11Device device)
        : ShaderMaterial(device, typeof(VertexColorMaterial).Assembly, "VertexColorMaterial.hlsl");
}
