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

        /// <summary>ワールド単位の長さをピクセル単位に直します。</summary>
        public static float ToPixels(float units) => units * PixelsPerUnit;

        /// <summary>
        /// ピクセル単位の実寸と中心位置から、1×1 の板を実際の大きさ・位置に置く行列を作ります。
        /// </summary>
        /// <param name="sizeInPixels">板の幅と高さ（ピクセル）。</param>
        /// <param name="centerInPixels">
        /// 板の中心が、アイテムの原点からどれだけずれているか（ピクセル、Y は下が正）。
        /// </param>
        /// <remarks>
        /// 中心がアイテムの原点と一致するとは限りません。テキストの文字揃えや、
        /// トリミングされた画像では描画範囲が原点から偏ります。
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

        /// <summary>
        /// YMM4 の Y 軸下向き・ピクセル単位の行列を、3D 空間の Y 軸上向き・
        /// ワールド単位の行列に直します。
        /// </summary>
        /// <remarks>
        /// 「3D回転」や「回り込みカメラ」といったエフェクトが
        /// <c>DrawDescription.Camera</c> に書き込む変換を、3D の形そのものに
        /// 掛けたいときに使います。
        /// </remarks>
        public static Matrix4x4 ToYUpMatrix(Matrix4x4 matrix)
        {
            // Y 軸を反転する基底変換 S * M * S（S = diag(1, -1, 1, 1)）は、
            // Y が絡む成分の符号を入れ替えることと同じ。
            matrix.M12 = -matrix.M12;
            matrix.M21 = -matrix.M21;
            matrix.M23 = -matrix.M23;
            matrix.M32 = -matrix.M32;

            matrix.M41 = ToWorld(matrix.M41);
            matrix.M42 = -ToWorld(matrix.M42);
            matrix.M43 = ToWorld(matrix.M43);

            return matrix;
        }
    }
}
