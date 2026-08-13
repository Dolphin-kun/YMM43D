using System.Numerics;
using YMM43D.Commons;

namespace YMM43D.Commons
{
    public static class ImageProjection
    {
        public static Matrix3x2 TangentToImage(float pixelsPerTangent, in ScreenPlacement placement)
        {
            var toScreen = Matrix3x2.CreateScale(pixelsPerTangent, -pixelsPerTangent);

            return toScreen * placement.ToImageSpace();
        }

        public static Matrix4x4 Compose(in Matrix3x2 tangentToImage, Vector2 imageOrigin, int width, int height)
        {
            var toNdc = Matrix3x2.CreateTranslation(-imageOrigin)
                      * Matrix3x2.CreateScale(2f / width, -2f / height)
                      * Matrix3x2.CreateTranslation(-1f, 1f);

            return SceneProjection.GetTangentProjection() * Lift(tangentToImage * toNdc);
        }

        private static Matrix4x4 Lift(in Matrix3x2 affine) => new(
            affine.M11, affine.M12, 0f, 0f,
            affine.M21, affine.M22, 0f, 0f,
            0f, 0f, 1f, 0f,
            affine.M31, affine.M32, 0f, 1f);
    }
}
