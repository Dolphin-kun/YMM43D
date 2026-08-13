using System.Numerics;
using YMM43D.Scene3D;

namespace YMM43D.Camera
{
    public static class SceneProjection
    {
        public const float DefaultFocalDistance = 10f;

        public const float DefaultFieldOfView = MathF.PI / 4f;

        private const float MinFieldOfViewDegrees = 1f;
        private const float MaxFieldOfViewDegrees = 179f;

        public const float NearPlane = 0.1f;

        private const float FarPlane = 1000f;

        public static float GetPixelsPerTangent(in CameraState camera, float screenHeight)
        {
            if (!camera.HasFieldOfView || !float.IsFinite(screenHeight) || screenHeight <= 0f)
                return WorldScale.PixelsPerUnit * DefaultFocalDistance;

            var degrees = Math.Clamp(camera.FieldOfView, MinFieldOfViewDegrees, MaxFieldOfViewDegrees);

            return screenHeight / (2f * MathF.Tan(Rotation3D.ToRadians(degrees) / 2f));
        }

        public static Matrix4x4 GetTangentProjection()
            => Matrix4x4.CreatePerspectiveOffCenter(
                -NearPlane, NearPlane, -NearPlane, NearPlane, NearPlane, FarPlane);

        public static Matrix4x4 GetProjectionMatrix(
            float aspectRatio, float screenHeight, float pixelsPerTangent)
            => Matrix4x4.CreatePerspectiveFieldOfView(
                GetFieldOfView(screenHeight, pixelsPerTangent), aspectRatio, NearPlane, FarPlane);

        public static float GetFieldOfView(float screenHeight, float pixelsPerTangent)
        {
            if (!float.IsFinite(screenHeight) || screenHeight <= 0f || pixelsPerTangent <= 0f)
                return DefaultFieldOfView;

            return 2f * MathF.Atan(screenHeight / (2f * pixelsPerTangent));
        }
    }
}
