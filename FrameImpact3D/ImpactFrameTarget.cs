using System.Numerics;
using Vortice.Direct3D11;

namespace FrameImpact3D
{
    internal sealed record ImpactFrameTarget(ID3D11RenderTargetView Target, ID3D11DepthStencilView? Depth, Vector2 Size);
}
