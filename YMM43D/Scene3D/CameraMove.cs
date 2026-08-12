using System.Numerics;

namespace YMM43D.Scene3D
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
    /// <param name="Distance">注視点までの距離の増減。</param>
    /// <param name="Target">注視点の移動量（ワールド単位）。</param>
    public readonly record struct CameraMove(
        float Yaw,
        float Pitch,
        float Roll,
        float Distance,
        Vector3 Target)
    {
        /// <summary>真上・真下を向いたときに視線が定まらなくなるのを避ける上限。</summary>
        public const float MaxPitch = 89.9f;

        /// <summary>カメラが注視点に重なると姿勢が決まらなくなるための下限。</summary>
        public const float MinDistance = 0.1f;

        /// <summary>何も動かさない差分。</summary>
        public static CameraMove None => default;

        /// <summary>動かす量があるかどうか。</summary>
        public bool IsZero => this == default;

        /// <summary>この差分を反映した設定値を返します。</summary>
        public CameraState ApplyTo(in CameraState state) => new(
            state.Yaw + Yaw,
            Math.Clamp(state.Pitch + Pitch, -MaxPitch, MaxPitch),
            state.Roll + Roll,
            MathF.Max(MinDistance, state.Distance + Distance),
            state.Target + Target);
    }
}
