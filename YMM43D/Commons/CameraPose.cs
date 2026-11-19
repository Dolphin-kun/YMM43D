using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct CameraPose(Vector3 Position, Vector3 Target, Vector3 Up, Matrix4x4 Rotation)
    {
        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up);

        public Matrix4x4 WorldMatrix => Rotation * Matrix4x4.CreateTranslation(Position);
    }
}
