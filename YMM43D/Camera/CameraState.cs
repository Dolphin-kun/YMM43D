using System.Numerics;
using YMM43D.Scene3D;

namespace YMM43D.Camera
{
    /// <summary>
    /// ある瞬間のカメラの設定値。
    /// </summary>
    /// <remarks>
    /// 置いた場所と向きが決まれば姿勢が決まります。キーフレームから取り出した
    /// 「その瞬間の値」をこの形にして受け渡すので、タイムライン上のカメラアイテムから
    /// 読んでも、プレビュー専用の視点から作っても同じように扱えます。
    /// </remarks>
    /// <param name="Position">カメラの位置（ワールド単位）。</param>
    /// <param name="Yaw">水平方向の回転角（度）。</param>
    /// <param name="Pitch">垂直方向の回転角（度）。</param>
    /// <param name="Roll">視線周りの回転角（度）。</param>
    /// <param name="FieldOfView">
    /// 垂直画角（度）。0 なら自動で、注視面が画面と1対1になる画角を使います。
    /// </param>
    public readonly record struct CameraState(
        Vector3 Position,
        float Yaw,
        float Pitch,
        float Roll,
        float FieldOfView)
    {
        /// <summary>真上・真下を向くと視線が定まらなくなるのを避ける上限。</summary>
        public const float MaxPitch = 89.9f;

        /// <summary>何も指定しなかったときのカメラ。原点を正面から見ます。</summary>
        public static CameraState Default
            => new(new Vector3(0f, 0f, SceneProjection.DefaultFocalDistance), 0f, 0f, 0f, 0f);

        /// <summary>画角を自分で決めているかどうか。</summary>
        public bool HasFieldOfView => FieldOfView > 0f;

        /// <summary>カメラの向き。</summary>
        public Matrix4x4 Rotation => Rotation3D.ForCamera(Yaw, Pitch, Roll);

        /// <summary>カメラが向いている方向の単位ベクトル。</summary>
        public Vector3 Forward => Vector3.Transform(new Vector3(0f, 0f, -1f), Rotation);

        /// <summary>この設定から、カメラの姿勢を組み立てます。</summary>
        public CameraPose GetPose()
        {
            var rotation = Rotation;
            var forward = Vector3.Transform(new Vector3(0f, 0f, -1f), rotation);
            var up = Vector3.Transform(Vector3.UnitY, rotation);

            return new CameraPose(Position, Position + forward, up, rotation);
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
                && MathF.Abs(FieldOfView - other.FieldOfView) < Epsilon
                && Vector3.DistanceSquared(Position, other.Position) < Epsilon * Epsilon;
        }
    }

    /// <summary>
    /// ある時点におけるカメラの姿勢。
    /// </summary>
    public readonly record struct CameraPose(Vector3 Position, Vector3 Target, Vector3 Up, Matrix4x4 Rotation)
    {
        /// <summary>この姿勢に対応するビュー行列。</summary>
        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up);

        /// <summary>カメラそのものを 3D 空間に描くためのワールド行列。</summary>
        public Matrix4x4 WorldMatrix => Rotation * Matrix4x4.CreateTranslation(Position);
    }
}
