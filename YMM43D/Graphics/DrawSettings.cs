using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public readonly record struct DrawSettings
    {
        public BlendMode Blend { get; init; }

        public FaceCulling Culling { get; init; }

        public bool IgnoreDepth { get; init; }

        public bool SkipDepthWrite { get; init; }

        public bool DepthOnly { get; init; }

        public ID3D11ShaderResourceView? Texture { get; init; }

        public ID3D11SamplerState? Sampler { get; init; }

        // 骨ごとの行列。骨で動かすモデルのときだけ入れる。
        public ID3D11ShaderResourceView? Bones { get; init; }

        // 表情で頂点をずらす量。表情のあるモデルのときだけ入れる。
        public ID3D11ShaderResourceView? Morphs { get; init; }
    }
}
