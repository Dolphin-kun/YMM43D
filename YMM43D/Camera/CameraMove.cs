using System.Numerics;

namespace YMM43D.Camera
{
    public readonly record struct CameraMove(float Yaw, float Pitch, float Roll, Vector3 Shift)
    {
        public static CameraMove None => default;

        public bool IsZero => this == default;

        public static CameraMove Rotate(float yaw, float pitch)
            => new(yaw, pitch, 0f, Vector3.Zero);

        public static CameraMove Translate(Vector3 shift)
            => new(0f, 0f, 0f, shift);

        public CameraState ApplyTo(in CameraState state) => state with
        {
            Position = state.Position + Shift,
            Yaw = state.Yaw + Yaw,
            Pitch = Math.Clamp(state.Pitch + Pitch, -CameraState.MaxPitch, CameraState.MaxPitch),
            Roll = state.Roll + Roll,
        };
    }
}
