using System.Numerics;

namespace YMM43D.Commons
{
    public static class Math
    {
        public static float ToRadians(float degrees) => degrees * (float)System.Math.PI / 180.0f;
        public static double ToRadians(double degrees) => degrees * System.Math.PI / 180.0;

        public static Matrix4x4 CreateCameraRotation(float yaw, float pitch, float roll)
        {
            var rYaw = ToRadians(yaw);
            var rPitch = ToRadians(pitch);
            var rRoll = ToRadians(roll);
            return Matrix4x4.CreateRotationZ(rRoll) * Matrix4x4.CreateRotationX(rPitch) * Matrix4x4.CreateRotationY(rYaw);
        }

        public static Matrix4x4 CreateObjectRotation(float rx, float ry, float rz)
        {
            var rX = ToRadians(rx);
            var rY = ToRadians(ry);
            var rZ = ToRadians(rz);
            return Matrix4x4.CreateRotationX(rX) * Matrix4x4.CreateRotationY(rY) * Matrix4x4.CreateRotationZ(rZ);
        }
    }
}
