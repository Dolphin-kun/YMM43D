using System.Numerics;

namespace FrameImpact3D
{
    internal readonly record struct ImpactFrameLook(ImpactFrameKind Kind, Vector3 Tint, float Blur, float Shift, float Glow, float Threshold, float Edge, ImpactFrameArea Area, ImpactAim Aim);
}
