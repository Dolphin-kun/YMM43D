using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// YMM4 のピクセル座標と、3D 空間のワールド単位を橋渡しします。
    /// </summary>
    /// <remarks>
    /// このライブラリでは、ワールドの 1 単位を 100 ピクセルとして扱います。
    /// アイテムの位置や大きさを 3D 空間に持ち込むときは、必ずこの換算を通してください。
    /// </remarks>
    public static class WorldScale
    {
        /// <summary>ワールド 1 単位に相当するピクセル数。</summary>
        public const float PixelsPerUnit = 100f;

        /// <summary>ピクセル単位の長さをワールド単位に直します。</summary>
        public static float ToWorld(float pixels) => pixels / PixelsPerUnit;

        /// <summary>
        /// ピクセル単位の実寸と中心位置から、1×1 の板を実際の大きさ・位置に置く行列を作ります。
        /// </summary>
        /// <param name="sizeInPixels">板の幅と高さ（ピクセル）。</param>
        /// <param name="centerInPixels">
        /// 板の中心が、アイテムの原点からどれだけずれているか（ピクセル、Y は下が正）。
        /// </param>
        /// <remarks>
        /// 中心がアイテムの原点と一致するとは限りません。テキストの文字揃えや、
        /// トリミングされた画像では、描画範囲が原点から偏ります。大きさだけを見て
        /// 中心を無視すると、揃え方を変えても同じ場所に表示されてしまいます。
        /// </remarks>
        public static Matrix4x4 CreateSizeMatrix(Vector2 sizeInPixels, Vector2 centerInPixels)
        {
            if (sizeInPixels.X <= 0 || sizeInPixels.Y <= 0)
                return Matrix4x4.Identity;

            // YMM4 の Y 軸は下向き、3D 空間は上向きなので中心のずれは符号を反転する。
            return Matrix4x4.CreateScale(ToWorld(sizeInPixels.X), ToWorld(sizeInPixels.Y), 1f)
                 * Matrix4x4.CreateTranslation(
                       ToWorld(centerInPixels.X), -ToWorld(centerInPixels.Y), 0f);
        }
    }
}
