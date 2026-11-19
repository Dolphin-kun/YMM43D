using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public sealed record ModelJoint(
        Vector3 ScalingPivot,
        Vector3 ScalingOffset,
        Vector3 RotationPivot,
        Vector3 RotationOffset,
        Matrix4x4 PreRotation,
        Matrix4x4 PostInverse,
        int Order)
    {
        public Matrix4x4 Compose(in Vector3 translation, in Vector3 angles, in Vector3 scale)
            => Matrix4x4.CreateTranslation(-ScalingPivot)
             * Matrix4x4.CreateScale(scale)
             * Matrix4x4.CreateTranslation(ScalingPivot)
             * Matrix4x4.CreateTranslation(ScalingOffset)
             * Matrix4x4.CreateTranslation(-RotationPivot)
             * PostInverse
             * Turn(angles, Order)
             * PreRotation
             * Matrix4x4.CreateTranslation(RotationPivot)
             * Matrix4x4.CreateTranslation(RotationOffset)
             * Matrix4x4.CreateTranslation(translation);

        public static Matrix4x4 Turn(in Vector3 degrees, int order)
        {
            var x = Matrix4x4.CreateRotationX(float.DegreesToRadians(degrees.X));
            var y = Matrix4x4.CreateRotationY(float.DegreesToRadians(degrees.Y));
            var z = Matrix4x4.CreateRotationZ(float.DegreesToRadians(degrees.Z));

            return order switch
            {
                1 => x * z * y,
                2 => y * z * x,
                3 => y * x * z,
                4 => z * x * y,
                5 => z * y * x,
                _ => x * y * z,
            };
        }
    }
}
