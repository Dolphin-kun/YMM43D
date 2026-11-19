using Vortice.Direct3D11;
using YMM43D.Graphics.Materials;

namespace PixelPoints3D
{
    internal sealed class PointCloudMaterial(ID3D11Device device) : ShaderMaterial(device, typeof(PointCloudMaterial).Assembly, "PointCloudVertex.hlsl", "VSMain", "PointCloudPixel.hlsl", "main");
}
