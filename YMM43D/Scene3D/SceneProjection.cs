using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// シーンを画面に写す射影。3Dプレビューと動画出力で共通です。
    /// </summary>
    /// <remarks>
    /// クリップ面はシーン全体で共通にします。アイテムごとに変えると、深度の目盛りが
    /// 揃わずアイテムをまたいだ前後関係が狂います。
    /// </remarks>
    public static class SceneProjection
    {
        /// <summary>画面の大きさが分からない場合に使う垂直画角（ラジアン）。</summary>
        public const float DefaultFieldOfView = MathF.PI / 4f;

        private const float NearPlane = 0.1f;
        private const float FarPlane = 1000f;

        /// <summary>
        /// 射影行列を返します。
        /// </summary>
        /// <param name="aspectRatio">描画先の横縦比。</param>
        /// <param name="screenHeight">動画の画面の高さ（ピクセル）。</param>
        /// <param name="distance">注視点までの距離。</param>
        public static Matrix4x4 GetProjectionMatrix(float aspectRatio, float screenHeight, float distance)
            => Matrix4x4.CreatePerspectiveFieldOfView(
                GetFieldOfView(screenHeight, distance), aspectRatio, NearPlane, FarPlane);

        /// <summary>
        /// 視線からの傾き（<c>x / -z</c>）をそのまま出す射影行列を返します。
        /// </summary>
        /// <remarks>
        /// 除算後の x・y が傾きそのものになります。ここから先は <see cref="ImageProjection"/> が
        /// 2D のアフィン変換として画面ピクセル・アイテムの画像・NDC へ順に移します。
        /// </remarks>
        public static Matrix4x4 GetTangentProjection()
            => Matrix4x4.CreatePerspectiveOffCenter(
                -NearPlane, NearPlane, -NearPlane, NearPlane, NearPlane, FarPlane);

        /// <summary>
        /// 視線からの傾き（正接）1 あたりのピクセル数を求めます。
        /// </summary>
        /// <remarks>
        /// 注視点の面（Z=0）にあるワールド1単位は、傾きにすると <c>1/距離</c> です。
        /// これに掛けた結果が <see cref="WorldScale.PixelsPerUnit"/> になるよう定めて
        /// いるので、Z=0 のアイテムは YMM4 が 2D で描く大きさと一致します。
        /// </remarks>
        public static float GetPixelsPerTangent(float distance)
            => WorldScale.PixelsPerUnit * MathF.Max(distance, CameraMove.MinDistance);

        /// <summary>
        /// 垂直画角（ラジアン）を求めます。
        /// </summary>
        /// <remarks>
        /// 画角は固定値ではなく、注視点の面が画面とちょうど1対1で対応するように決めます。
        /// この決め方だと、距離は「寄り引き」ではなく「遠近の強さ」を決めるつまみに
        /// なります。近づけるほど画角が広がり、手前と奥の差が強く出ます。
        /// </remarks>
        public static float GetFieldOfView(float screenHeight, float distance)
        {
            // 画面の大きさが取れない場面では既定値に戻す。
            if (!float.IsFinite(screenHeight) || screenHeight <= 0)
                return DefaultFieldOfView;

            return 2f * MathF.Atan(screenHeight / (2f * GetPixelsPerTangent(distance)));
        }
    }
}
