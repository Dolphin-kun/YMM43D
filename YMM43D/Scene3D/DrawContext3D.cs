using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Graphics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// 1つのアイテムを 3D 空間に描画するために、YMM4 のアイテム設定から
    /// 組み立てられた情報。<see cref="Plugin.I3DProvider.Draw"/> に渡されます。
    /// </summary>
    public sealed class DrawContext3D
    {
        /// <summary>
        /// アイテムの位置・拡大率・回転を反映したワールド行列。
        /// プロバイダーは自分の形状固有の変換をこの行列の前に掛けます。
        /// </summary>
        public required Matrix4x4 World { get; init; }

        /// <summary>0.0〜1.0 の不透明度。フェードイン・フェードアウトも反映済みです。</summary>
        public required float Opacity { get; init; }

        /// <summary>合成方法。</summary>
        public BlendMode Blend { get; init; }

        /// <summary>YMM4 の「最前面に表示」。深度テストを無効にして描画します。</summary>
        public bool IsAlwaysOnTop { get; init; }

        /// <summary>アイテム内での時間位置。<see cref="AnimationExtensions"/> と組み合わせて使います。</summary>
        public required FrameContext Time { get; init; }

        /// <summary>
        /// アイテムの 2D 描画結果をテクスチャ化したもの。
        /// <see cref="Plugin.I3DProvider.RequiresMappedTexture"/> が <c>false</c> の場合は
        /// <c>null</c> になります。
        /// </summary>
        /// <remarks>
        /// このテクスチャの寿命は呼び出し元が管理します。プロバイダー側で破棄しないでください。
        /// </remarks>
        public ID3D11ShaderResourceView? Texture { get; init; }

        /// <summary>
        /// <see cref="Blend"/> と <see cref="IsAlwaysOnTop"/> を反映した
        /// <see cref="DrawSettings"/> を作ります。カリングとテクスチャは呼び出し側が足します。
        /// </summary>
        public DrawSettings ToDrawSettings(FaceCulling culling = FaceCulling.None, ID3D11ShaderResourceView? texture = null) => new()
        {
            Blend = Blend,
            IgnoreDepth = IsAlwaysOnTop,
            Culling = culling,
            Texture = texture ?? Texture,
        };
    }
}
