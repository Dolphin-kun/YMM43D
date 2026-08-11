using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// 度数法の角度から回転行列を作るヘルパー。
    /// </summary>
    /// <remarks>
    /// カメラとオブジェクトでは回転の適用順が異なります。取り違えると
    /// ジンバルの挙動が変わってしまうため、用途ごとにメソッドを分けています。
    /// </remarks>
    public static class Rotation3D
    {
        public static float ToRadians(float degrees) => degrees * MathF.PI / 180f;

        /// <summary>
        /// カメラ向けの回転行列。ロール → ピッチ → ヨー の順に適用します。
        /// </summary>
        public static Matrix4x4 ForCamera(float yaw, float pitch, float roll)
            => Matrix4x4.CreateRotationZ(ToRadians(roll))
             * Matrix4x4.CreateRotationX(ToRadians(pitch))
             * Matrix4x4.CreateRotationY(ToRadians(yaw));

        /// <summary>
        /// オブジェクト向けの回転行列。X → Y → Z の順に適用します。
        /// </summary>
        public static Matrix4x4 ForObject(float x, float y, float z)
            => Matrix4x4.CreateRotationX(ToRadians(x))
             * Matrix4x4.CreateRotationY(ToRadians(y))
             * Matrix4x4.CreateRotationZ(ToRadians(z));
    }
}
