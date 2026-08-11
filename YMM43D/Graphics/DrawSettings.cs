using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    /// <summary>
    /// 1回の描画呼び出しごとに変わるパイプライン設定。
    /// </summary>
    /// <remarks>
    /// 既定値（<c>default</c>）は「通常合成・カリングなし・深度テストあり・テクスチャなし」です。
    /// </remarks>
    public readonly record struct DrawSettings
    {
        /// <summary>合成方法。</summary>
        public BlendMode Blend { get; init; }

        /// <summary>カリング方法。</summary>
        public FaceCulling Culling { get; init; }

        /// <summary>
        /// <c>true</c> のとき深度テストを無効化し、常に手前に描画します。
        /// YMM4 の「最前面に表示」に対応します。
        /// </summary>
        public bool IgnoreDepth { get; init; }

        /// <summary>ピクセルシェーダーのスロット 0 に設定するテクスチャ。</summary>
        public ID3D11ShaderResourceView? Texture { get; init; }

        /// <summary>
        /// テクスチャのサンプラー。<c>null</c> かつ <see cref="Texture"/> がある場合は
        /// リニア補間のサンプラーが使われます。
        /// </summary>
        public ID3D11SamplerState? Sampler { get; init; }
    }
}
