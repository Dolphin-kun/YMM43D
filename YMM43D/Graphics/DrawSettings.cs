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

        /// <summary>
        /// <c>true</c> のとき、深度バッファへの書き込みを行いません。
        /// </summary>
        /// <remarks>
        /// 半透明な板を描くときに使います。書き込むと透明な部分まで深度を持ち、後ろの
        /// ものが抜けて見えなくなります。深度テスト自体は行うので立体物との前後関係は
        /// 保たれ、板同士の重なりは描画順で決まります。
        /// </remarks>
        public bool SkipDepthWrite { get; init; }

        /// <summary>
        /// <c>true</c> のとき、色を書かずに深度だけを書き込みます。
        /// </summary>
        /// <remarks>
        /// 自分を描く前に他の 3D 物体をこの設定で描いておくと、後から描く自分が
        /// 深度テストで正しく隠されます。シェーダーはそのまま使えます。
        /// </remarks>
        public bool DepthOnly { get; init; }

        /// <summary>ピクセルシェーダーのスロット 0 に設定するテクスチャ。</summary>
        public ID3D11ShaderResourceView? Texture { get; init; }

        /// <summary>
        /// テクスチャのサンプラー。<c>null</c> かつ <see cref="Texture"/> がある場合は
        /// リニア補間のサンプラーが使われます。
        /// </summary>
        public ID3D11SamplerState? Sampler { get; init; }
    }
}
