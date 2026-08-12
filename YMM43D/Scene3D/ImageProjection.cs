using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// 3D 空間から、1つのアイテムの出力画像へ写す射影を組み立てます。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 経路は次のとおりです。
    /// </para>
    /// <code>
    /// ワールド → 視線からの傾き → 画面ピクセル → アイテムの画像 → NDC
    /// </code>
    /// <para>
    /// 「傾き」より後ろはすべて 2D のアフィン変換なので、1枚の <see cref="Matrix3x2"/> に
    /// まとめられます。それを射影行列に畳み込むことで、Direct2D 側の仕事は
    /// 「ビットマップをオフセットに置くだけ」になります。以前は位置・回転・拡大率を
    /// <c>DrawImage</c> のオフセット・<c>DeviceContext.Transform</c>・射影の縮尺という
    /// 3つの別々の仕組みで打ち消しており、互いの干渉を確かめる術がありませんでした。
    /// </para>
    /// <para>
    /// 畳み込む変換が触るのは x と y だけです。深度はどのアイテムの絵でも同じ値になるため、
    /// アイテムをまたいだ前後関係が食い違いません。切り出す範囲が違っても深度が変わらない、
    /// というのがこの組み立ての要です。
    /// </para>
    /// </remarks>
    public static class ImageProjection
    {
        /// <summary>
        /// 視線からの傾きを、そのアイテムの画像の中の位置（ピクセル）に移す変換を返します。
        /// </summary>
        /// <param name="pixelsPerTangent">傾き1あたりの画面ピクセル数。</param>
        /// <param name="placement">YMM4 が画像に掛ける配置。</param>
        public static Matrix3x2 TangentToImage(float pixelsPerTangent, in ScreenPlacement placement)
        {
            // 3D の Y は上向き、画面の Y は下向き。
            var toScreen = Matrix3x2.CreateScale(pixelsPerTangent, -pixelsPerTangent);

            return toScreen * placement.ToImageSpace();
        }

        /// <summary>
        /// 最終的な射影行列を組み立てます。
        /// </summary>
        /// <param name="tangentToImage"><see cref="TangentToImage"/> で得た変換。</param>
        /// <param name="imageOrigin">切り出す範囲の左上（画像の中の位置）。</param>
        /// <param name="width">描画先の幅（ピクセル）。</param>
        /// <param name="height">描画先の高さ（ピクセル）。</param>
        public static Matrix4x4 Compose(in Matrix3x2 tangentToImage, Vector2 imageOrigin, int width, int height)
        {
            // 切り出した範囲を NDC（-1〜1、Y は上向き）に合わせる。
            var toNdc = Matrix3x2.CreateTranslation(-imageOrigin)
                      * Matrix3x2.CreateScale(2f / width, -2f / height)
                      * Matrix3x2.CreateTranslation(-1f, 1f);

            return SceneCamera.GetTangentProjection() * Lift(tangentToImage * toNdc);
        }

        /// <summary>
        /// 2D のアフィン変換を、クリップ座標に掛けられる形に持ち上げます。
        /// </summary>
        /// <remarks>
        /// クリップ座標の x と y だけを動かし、z と w には触れません。除算前の w を
        /// 平行移動の係数に使うので、遠近を保ったまま画面上の 2D 変換として働きます。
        /// </remarks>
        private static Matrix4x4 Lift(in Matrix3x2 affine) => new(
            affine.M11, affine.M12, 0f, 0f,
            affine.M21, affine.M22, 0f, 0f,
            0f, 0f, 1f, 0f,
            affine.M31, affine.M32, 0f, 1f);
    }
}
