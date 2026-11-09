using Vortice.Direct3D11;

namespace YMM43D.Graphics.Materials
{
    public sealed class ModelMaterial(ID3D11Device device)
        : ShaderMaterial(device, typeof(ModelMaterial).Assembly, "ModelMaterial.hlsl");
}
