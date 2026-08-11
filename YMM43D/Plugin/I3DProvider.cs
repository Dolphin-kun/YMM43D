using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Scene3D;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D空間に何かを描画できるオブジェクト。このライブラリの中心となる拡張点です。
    /// </summary>
    /// <remarks>
    /// 図形アイテムのソース、映像エフェクトのプロセッサなどがこれを実装すると、
    /// 3Dプレビューと動画出力の両方から呼ばれるようになります。
    /// </remarks>
    public interface I3DProvider
    {
        /// <summary>
        /// 描画にアイテム本来の 2D 画像が必要な場合は <c>true</c> を返します。
        /// </summary>
        /// <remarks>
        /// <c>true</c> のとき、<see cref="DrawContext3D.Texture"/> にアイテムの描画結果が
        /// テクスチャとして渡されます。テクスチャ化にはコストがかかるため、
        /// 自前の形状だけを描くプロバイダーは <c>false</c> のままにしてください。
        /// </remarks>
        bool RequiresMappedTexture { get; }

        /// <summary>
        /// 3D空間にこのオブジェクトを描画します。
        /// </summary>
        /// <param name="render">描画先とカメラ行列。</param>
        /// <param name="item">描画するアイテムの変換・不透明度・時間位置など。</param>
        void Draw(in Render3DContext render, DrawContext3D item);
    }

    /// <summary>
    /// 自前で用意したテクスチャを 3D 描画に提供できるオブジェクト。
    /// </summary>
    /// <remarks>
    /// 映像エフェクトのように、入力画像を自分で保持しているプロバイダーが実装します。
    /// これを実装していると、呼び出し側はアイテムの画像をテクスチャ化する処理を省きます。
    /// </remarks>
    public interface I3DTextureProvider
    {
        /// <summary>
        /// 指定デバイス上で使えるテクスチャを返します。無ければ <c>null</c>。
        /// </summary>
        /// <remarks>
        /// 戻り値の寿命は実装側が管理します。呼び出し側は破棄しません。
        /// </remarks>
        ID3D11ShaderResourceView? GetTexture(ID3D11Device device);
    }

    /// <summary>
    /// 描画される内容の実寸を伝えられるオブジェクト。
    /// </summary>
    /// <remarks>
    /// アイテムの画像がトリミングされている場合など、ワールド行列を組み立てる側が
    /// 正しい大きさと中心のずれを知る必要があるときに使います。
    /// </remarks>
    public interface I3DSizeProvider
    {
        /// <summary>
        /// 実寸が分かる場合は <c>true</c> を返し、大きさと原点からのずれを設定します。
        /// </summary>
        /// <param name="size">幅と高さ（ピクセル）。</param>
        /// <param name="offset">原点からのずれ（ピクセル）。</param>
        bool TryGetSize(out Vector2 size, out Vector2 offset);
    }

    /// <summary>
    /// 自分が使っているワールド行列を答えられるオブジェクト。
    /// </summary>
    /// <remarks>
    /// アイテムをまたいだ前後関係を出すとき、他のアイテムをどこに置くかを決めるのに使います。
    /// アイテムの配置（位置・拡大率・回転）は呼び出し側が知っているので、それを<b>除いた</b>
    /// 自分自身の変換だけを返してください。
    /// <para>
    /// 実寸だけなら <see cref="I3DSizeProvider"/> で足りますが、エフェクトが
    /// <c>DrawDescription.Camera</c> を取り込んでいる場合など、実寸から組み立て直せない
    /// 変換を持っているときはこちらを実装します。
    /// </para>
    /// </remarks>
    public interface I3DLocalTransform
    {
        /// <summary>変換が分かる場合は <c>true</c> を返し、行列を設定します。</summary>
        bool TryGetLocalMatrix(out Matrix4x4 matrix);
    }

    /// <summary>
    /// 3D対応の映像エフェクトであることを示すインターフェース。
    /// 3D描画とテクスチャ提供の両方を担います。
    /// </summary>
    public interface I3DVideoEffect : I3DProvider, I3DTextureProvider;
}
