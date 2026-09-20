using Vortice.Direct3D11;

namespace YMM43D.Graphics.Materials
{
    // 骨で動かすモデル用。頂点ごとの骨の割り当てを見て、位置と法線を動かしてから塗る。
    public sealed class SkinnedTextureMaterial(ID3D11Device device)
        : ShaderMaterial(device, typeof(SkinnedTextureMaterial).Assembly, "SkinnedTextureMaterial.hlsl");
}
