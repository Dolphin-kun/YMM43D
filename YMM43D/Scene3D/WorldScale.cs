using System.Numerics;

namespace YMM43D.Scene3D
{
    public static class WorldScale
    {
        public const float PixelsPerUnit = 100f;

        public static float ToWorld(float pixels) => pixels / PixelsPerUnit;

        public static float ToPixels(float units) => units * PixelsPerUnit;

        public static Matrix4x4 CreateSizeMatrix(Vector2 sizeInPixels, Vector2 centerInPixels)
            => CreateSizeMatrix(sizeInPixels, centerInPixels, Matrix4x4.Identity);

        public static Matrix4x4 CreateSizeMatrix(
            Vector2 sizeInPixels, Vector2 centerInPixels, in Matrix4x4 rotation)
        {
            if (sizeInPixels.X <= 0 || sizeInPixels.Y <= 0)
                return rotation;

            return Matrix4x4.CreateScale(ToWorld(sizeInPixels.X), ToWorld(sizeInPixels.Y), 1f)
                 * rotation
                 * Matrix4x4.CreateTranslation(
                       ToWorld(centerInPixels.X), -ToWorld(centerInPixels.Y), 0f);
        }

        public static Matrix4x4 ToYUpMatrix(Matrix4x4 matrix)
        {
            matrix.M12 = -matrix.M12;
            matrix.M21 = -matrix.M21;
            matrix.M23 = -matrix.M23;
            matrix.M32 = -matrix.M32;

            matrix.M41 = ToWorld(matrix.M41);
            matrix.M42 = -ToWorld(matrix.M42);
            matrix.M43 = ToWorld(matrix.M43);

            return matrix;
        }
    }
}
