using Vortice.Direct3D11;

namespace YMM43D.Graphics.Materials
{
    public sealed class SkinnedModelMaterial(ID3D11Device device)
        : ShaderMaterial(device, typeof(SkinnedModelMaterial).Assembly, "SkinnedModelMaterial.hlsl");
}
