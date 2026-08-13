using System.Numerics;

namespace YMM43D.Scene3D
{
    public readonly record struct CameraState(
        Vector3 Position,
        float Yaw,
        float Pitch,
        float Roll,
        float FieldOfView)
    {
        public const float MaxPitch = 89.9f;

        public static CameraState Default
            => new(new Vector3(0f, 0f, SceneProjection.DefaultFocalDistance), 0f, 0f, 0f, 0f);

        public bool HasFieldOfView => FieldOfView > 0f;

        public Matrix4x4 Rotation => Rotation3D.ForCamera(Yaw, Pitch, Roll);

        public Vector3 Forward => Vector3.Transform(new Vector3(0f, 0f, -1f), Rotation);

        public CameraPose GetPose()
        {
            var rotation = Rotation;
            var forward = Vector3.Transform(new Vector3(0f, 0f, -1f), rotation);
            var up = Vector3.Transform(Vector3.UnitY, rotation);

            return new CameraPose(Position, Position + forward, up, rotation);
        }

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

    public readonly record struct CameraPose(Vector3 Position, Vector3 Target, Vector3 Up, Matrix4x4 Rotation)
    {
        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up);

        public Matrix4x4 WorldMatrix => Rotation * Matrix4x4.CreateTranslation(Position);
    }
}
