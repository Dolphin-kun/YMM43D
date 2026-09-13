using System.Numerics;

namespace YMM43D.Commons
{
    public static class Rotation3D
    {
        public static float Wrap(float degrees) => degrees - 360f * MathF.Round(degrees / 360f);

        public static Matrix4x4 ForCamera(float yaw, float pitch, float roll)
            => Matrix4x4.CreateRotationZ(float.DegreesToRadians(roll))
             * Matrix4x4.CreateRotationX(float.DegreesToRadians(pitch))
             * Matrix4x4.CreateRotationY(float.DegreesToRadians(yaw));

        public static Matrix4x4 ForObject(float x, float y, float z)
            => Matrix4x4.CreateRotationX(float.DegreesToRadians(x))
             * Matrix4x4.CreateRotationY(float.DegreesToRadians(y))
             * Matrix4x4.CreateRotationZ(float.DegreesToRadians(z));
    }
}
