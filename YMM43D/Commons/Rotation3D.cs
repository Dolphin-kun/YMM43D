using System.Numerics;

namespace YMM43D.Commons
{
    public static class Rotation3D
    {
        public static float ToRadians(float degrees) => degrees * MathF.PI / 180f;

        public static float ToDegrees(float radians) => radians * 180f / MathF.PI;

        public static float Wrap(float degrees) => degrees - 360f * MathF.Round(degrees / 360f);

        public static Matrix4x4 ForCamera(float yaw, float pitch, float roll)
            => Matrix4x4.CreateRotationZ(ToRadians(roll))
             * Matrix4x4.CreateRotationX(ToRadians(pitch))
             * Matrix4x4.CreateRotationY(ToRadians(yaw));

        public static Matrix4x4 ForObject(float x, float y, float z)
            => Matrix4x4.CreateRotationX(ToRadians(x))
             * Matrix4x4.CreateRotationY(ToRadians(y))
             * Matrix4x4.CreateRotationZ(ToRadians(z));
    }
}
