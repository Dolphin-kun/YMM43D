using System.Numerics;
using YMM43D.Scene3D;

namespace YMM43D.Camera
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
        /// <summary>
        /// 画角を自動で決めるときに、画面と1対1で対応させる面までの距離。
        /// </summary>
        /// <remarks>
        /// カメラからこの距離にある面では、ワールド1単位が
        /// <see cref="WorldScale.PixelsPerUnit"/> ちょうどになります。つまり Z=0 に
        /// 置いたアイテムは、YMM4 が 2D で描く大きさとぴったり一致します。
        /// </remarks>
        public const float DefaultFocalDistance = 10f;

        /// <summary>画面の大きさが分からない場合に使う垂直画角（ラジアン）。</summary>
        public const float DefaultFieldOfView = MathF.PI / 4f;

        private const float MinFieldOfViewDegrees = 1f;
        private const float MaxFieldOfViewDegrees = 179f;

        private const float NearPlane = 0.1f;
        private const float FarPlane = 1000f;

        /// <summary>
        /// 視線からの傾き（正接）1 あたりのピクセル数を求めます。
        /// </summary>
        /// <param name="screenHeight">動画の画面の高さ（ピクセル）。</param>
        /// <remarks>
        /// カメラが画角を持っていればそこから、持っていなければ
        /// <see cref="DefaultFocalDistance"/> の面が1対1になる値を返します。
        /// </remarks>
        public static float GetPixelsPerTangent(in CameraState camera, float screenHeight)
        {
            if (!camera.HasFieldOfView || !float.IsFinite(screenHeight) || screenHeight <= 0f)
                return WorldScale.PixelsPerUnit * DefaultFocalDistance;

            var degrees = Math.Clamp(camera.FieldOfView, MinFieldOfViewDegrees, MaxFieldOfViewDegrees);

            return screenHeight / (2f * MathF.Tan(Rotation3D.ToRadians(degrees) / 2f));
        }

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
        /// 射影行列を返します。
        /// </summary>
        /// <param name="aspectRatio">描画先の横縦比。</param>
        /// <param name="screenHeight">動画の画面の高さ（ピクセル）。</param>
        /// <param name="pixelsPerTangent">傾き 1 あたりのピクセル数。</param>
        public static Matrix4x4 GetProjectionMatrix(
            float aspectRatio, float screenHeight, float pixelsPerTangent)
            => Matrix4x4.CreatePerspectiveFieldOfView(
                GetFieldOfView(screenHeight, pixelsPerTangent), aspectRatio, NearPlane, FarPlane);

        /// <summary>
        /// 垂直画角（ラジアン）を求めます。
        /// </summary>
        public static float GetFieldOfView(float screenHeight, float pixelsPerTangent)
        {
            // 画面の大きさが取れない場面では既定値に戻す。
            if (!float.IsFinite(screenHeight) || screenHeight <= 0f || pixelsPerTangent <= 0f)
                return DefaultFieldOfView;

            return 2f * MathF.Atan(screenHeight / (2f * pixelsPerTangent));
        }
    }
}
