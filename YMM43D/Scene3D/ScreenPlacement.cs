using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// YMM4 が出来上がった画像に掛ける 2D 配置。
    /// </summary>
    /// <remarks>
    /// YMM4 はアイテムの画像を「拡大 → 回転 → 平行移動」の順に置きます。3D 側でも同じ
    /// 配置をワールド行列に取り込んでいるため、そのままでは二重に掛かります。この型は
    /// その配置と、打ち消すための逆変換を表します。
    /// <para>
    /// 映像エフェクトは <c>DrawDescription</c> を無効化して YMM4 に配置させないので
    /// <see cref="None"/> になります。図形アイテムには返す口が無いため、実際の配置を
    /// 持たせて打ち消します。
    /// </para>
    /// </remarks>
    /// <param name="Offset">アイテムの位置（画面ピクセル。Y は下向き）。</param>
    /// <param name="Zoom">拡大率（1.0 で等倍）。</param>
    /// <param name="RotationDegrees">回転角（度）。画面上で時計回りが正。</param>
    /// <param name="Depth">アイテムの Z（画面ピクセル）。正で手前。</param>
    public readonly record struct ScreenPlacement(
        Vector2 Offset,
        float Zoom,
        float RotationDegrees,
        float Depth)
    {
        /// <summary>
        /// YMM4 が Z を遠近に変換するときの基準距離（ピクセル）。
        /// </summary>
        /// <remarks>
        /// YMM4 はアイテムを <c>基準距離 / (基準距離 - Z)</c> 倍して描きます。実測すると
        /// Z=500 でちょうど 2 倍になったので、基準距離は 1000px です。既定のカメラ距離
        /// （10 単位 ＝ 1000px）と一致するので、既定の設定では YMM4 の 2D の遠近と
        /// 3D 側の投影がまったく同じ式になります。
        /// </remarks>
        public const float HostPerspectiveDistance = 1000f;

        /// <summary>遠近の倍率として認める上限。カメラ位置に近づくと発散するため。</summary>
        private const float MaxPerspectiveScale = 100f;

        /// <summary>YMM4 が何もしないことを表す値。</summary>
        public static ScreenPlacement None => new(Vector2.Zero, 1f, 0f, 0f);

        /// <summary>
        /// YMM4 が Z から求める拡大の倍率。
        /// </summary>
        public float PerspectiveScale
        {
            get
            {
                if (!float.IsFinite(Depth))
                    return 1f;

                var remaining = HostPerspectiveDistance - Depth;

                return remaining >= HostPerspectiveDistance / MaxPerspectiveScale
                    ? HostPerspectiveDistance / remaining
                    : MaxPerspectiveScale;
            }
        }

        /// <summary>
        /// 画面上の真の位置を、このアイテムの画像の中での位置に移す変換を返します。
        /// </summary>
        /// <remarks>
        /// YMM4 が後から掛ける配置のちょうど逆です。この変換を通した絵を渡せば、
        /// YMM4 が配置した結果が本来あるべき位置に戻ります。
        /// <para>
        /// YMM4 の順序は「拡大率 → 回転 → 位置 → Z の遠近」です。Z の遠近だけは
        /// 位置にも掛かるため、逆をたどるときは最初に外します。
        /// </para>
        /// </remarks>
        public Matrix3x2 ToImageSpace()
        {
            var zoom = float.IsFinite(Zoom) && Zoom > 0f ? Zoom : 1f;

            // YMM4 の Y は下向きなので、時計回りの回転はそのまま正の角になる。
            var radians = Rotation3D.ToRadians(RotationDegrees);

            return Matrix3x2.CreateScale(1f / PerspectiveScale)
                 * Matrix3x2.CreateTranslation(-Offset)
                 * Matrix3x2.CreateRotation(-radians)
                 * Matrix3x2.CreateScale(1f / zoom);
        }
    }
}
