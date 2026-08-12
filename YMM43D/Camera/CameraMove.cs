using System.Numerics;

namespace YMM43D.Camera
{
    /// <summary>
    /// カメラをどれだけ動かすか、という差分。
    /// </summary>
    /// <remarks>
    /// プレビュー上のドラッグは、絶対値ではなく差分としてカメラへ伝えます。
    /// キーフレームを打ったカメラに絶対値を書き込むと打った動きが消えますが、
    /// 差分なら全部のキーフレームを同じだけずらせば済み、動きが残ります。
    /// </remarks>
    /// <param name="Yaw">水平方向の回転角の増減（度）。</param>
    /// <param name="Pitch">垂直方向の回転角の増減（度）。</param>
    /// <param name="Roll">視線周りの回転角の増減（度）。</param>
    /// <param name="Shift">位置の移動量（ワールド単位）。</param>
    public readonly record struct CameraMove(float Yaw, float Pitch, float Roll, Vector3 Shift)
    {
        /// <summary>何も動かさない差分。</summary>
        public static CameraMove None => default;

        /// <summary>動かす量があるかどうか。</summary>
        public bool IsZero => this == default;

        /// <summary>回転だけの差分。</summary>
        public static CameraMove Rotate(float yaw, float pitch)
            => new(yaw, pitch, 0f, Vector3.Zero);

        /// <summary>移動だけの差分。</summary>
        public static CameraMove Translate(Vector3 shift)
            => new(0f, 0f, 0f, shift);

        /// <summary>この差分を反映した設定値を返します。</summary>
        public CameraState ApplyTo(in CameraState state) => state with
        {
            Position = state.Position + Shift,
            Yaw = state.Yaw + Yaw,
            Pitch = Math.Clamp(state.Pitch + Pitch, -CameraState.MaxPitch, CameraState.MaxPitch),
            Roll = state.Roll + Roll,
        };
    }
}
