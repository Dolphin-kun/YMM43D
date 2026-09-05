using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Graphics;

namespace YMM43D.Commons
{
    public sealed class DrawContext3D
    {
        public const float SolidAlpha = 0.5f;

        public const float VisibleAlpha = 1f / 512f;

        public required Matrix4x4 World { get; init; }

        public required float Opacity { get; init; }

        public BlendMode Blend { get; init; }

        public bool IsAlwaysOnTop { get; init; }

        public bool DepthOnly { get; init; }

        public required FrameContext Time { get; init; }

        public float AlphaCutoff => DepthOnly ? SolidAlpha : VisibleAlpha;

        public ID3D11ShaderResourceView? Texture { get; init; }

        public DrawSettings ToDrawSettings(FaceCulling culling = FaceCulling.None, ID3D11ShaderResourceView? texture = null) => new()
        {
            Blend = Blend,
            IgnoreDepth = IsAlwaysOnTop,
            DepthOnly = DepthOnly,
            Culling = culling,
            Texture = texture ?? Texture,
        };
    }
}
