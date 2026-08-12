using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// YMM4 が出来上がった画像に掛ける 2D 配置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// YMM4 はアイテムの画像を「拡大 → 回転 → 平行移動」の順に置きます。3D 側でも
    /// 同じ配置をワールド行列に取り込んでいるため、そのままでは二重に掛かります。
    /// この型はその配置と、打ち消すための逆変換を表します。
    /// </para>
    /// <para>
    /// 映像エフェクトは <c>DrawDescription</c> を無効化して YMM4 に配置させないので
    /// <see cref="None"/> になります。図形アイテムには返す口が無いため、実際の配置を
    /// 持たせて打ち消します。
    /// </para>
    /// </remarks>
    /// <param name="Offset">アイテムの位置（画面ピクセル。Y は下向き）。</param>
    /// <param name="Zoom">拡大率（1.0 で等倍）。</param>
    /// <param name="RotationDegrees">回転角（度）。画面上で時計回りが正。</param>
    public readonly record struct ScreenPlacement(Vector2 Offset, float Zoom, float RotationDegrees)
    {
        /// <summary>YMM4 が何もしないことを表す値。</summary>
        public static ScreenPlacement None => new(Vector2.Zero, 1f, 0f);

        /// <summary>
        /// 画面上の真の位置を、このアイテムの画像の中での位置に移す変換を返します。
        /// </summary>
        /// <remarks>
        /// YMM4 が後から掛ける配置のちょうど逆です。この変換を通した絵を渡せば、
        /// YMM4 が配置した結果が本来あるべき位置に戻ります。
        /// </remarks>
        public Matrix3x2 ToImageSpace()
        {
            var zoom = float.IsFinite(Zoom) && Zoom > 0f ? Zoom : 1f;

            // YMM4 の Y は下向きなので、時計回りの回転はそのまま正の角になる。
            var radians = Rotation3D.ToRadians(RotationDegrees);

            return Matrix3x2.CreateTranslation(-Offset)
                 * Matrix3x2.CreateRotation(-radians)
                 * Matrix3x2.CreateScale(1f / zoom);
        }
    }
}
