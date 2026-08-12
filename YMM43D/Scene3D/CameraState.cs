using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// ある瞬間のカメラの設定値。
    /// </summary>
    /// <remarks>
    /// カメラは注視点のまわりを回る軌道カメラです。角度と距離と注視点が決まれば
    /// 姿勢が決まるので、キーフレームから取り出した「その瞬間の値」をこの形にして
    /// 受け渡します。<see cref="SceneCamera"/> のようなオブジェクトを持ち回らないので、
    /// タイムライン上のカメラアイテムから読んでも、プレビュー専用の視点から作っても
    /// 同じように扱えます。
    /// </remarks>
    /// <param name="Yaw">水平方向の回転角（度）。</param>
    /// <param name="Pitch">垂直方向の回転角（度）。</param>
    /// <param name="Roll">視線周りの回転角（度）。</param>
    /// <param name="Distance">注視点からカメラまでの距離。遠近の強さを決めます。</param>
    /// <param name="Target">カメラが向く先。</param>
    public readonly record struct CameraState(
        float Yaw,
        float Pitch,
        float Roll,
        float Distance,
        Vector3 Target)
    {
        /// <summary>何も指定しなかったときのカメラ。</summary>
        public static CameraState Default => new(0f, 0f, 0f, 10f, Vector3.Zero);

        /// <summary>この設定から、カメラの姿勢を組み立てます。</summary>
        public CameraPose GetPose()
        {
            var rotation = Rotation3D.ForCamera(Yaw, Pitch, Roll);

            var forward = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var up = Vector3.Transform(Vector3.UnitY, rotation);
            var position = Target - forward * Distance;

            return new CameraPose(position, Target, up, rotation);
        }

        /// <summary>見え方が同じとみなせるほど近いかどうか。</summary>
        /// <remarks>
        /// カメラが動いたかどうかの判定に使います。値そのものの比較では、
        /// キーフレーム補間の丸め誤差で毎フレーム「動いた」ことになってしまいます。
        /// </remarks>
        public bool NearlyEquals(in CameraState other)
        {
            const float Epsilon = 0.0001f;

            return MathF.Abs(Yaw - other.Yaw) < Epsilon
                && MathF.Abs(Pitch - other.Pitch) < Epsilon
                && MathF.Abs(Roll - other.Roll) < Epsilon
                && MathF.Abs(Distance - other.Distance) < Epsilon
                && Vector3.DistanceSquared(Target, other.Target) < Epsilon * Epsilon;
        }
    }
}
