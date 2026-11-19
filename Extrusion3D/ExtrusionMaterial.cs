using Vortice.Direct3D11;
using YMM43D.Graphics.Materials;

namespace Extrusion3D
{
    internal sealed class ExtrusionMaterial(ID3D11Device device) : ShaderMaterial(device, typeof(ExtrusionMaterial).Assembly, "ExtrusionVertex.hlsl", "VSMain", "ExtrusionPixel.hlsl", "main");
}
