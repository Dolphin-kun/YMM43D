using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct ScreenPlacement(
        Vector2 Offset,
        float Zoom,
        float RotationDegrees,
        float Depth)
    {
        public const float HostPerspectiveDistance = 1000f;

        private const float MaxPerspectiveScale = 100f;

        public static ScreenPlacement None => new(Vector2.Zero, 1f, 0f, 0f);

        public float PerspectiveScale
        {
            get
            {
                if (!float.IsFinite(Depth))
                    return 1f;

                var remaining = HostPerspectiveDistance - Depth;

                return remaining >= HostPerspectiveDistance / MaxPerspectiveScale
                    ? HostPerspectiveDistance / remaining
                    : MaxPerspectiveScale;
            }
        }

        public Matrix3x2 ToImageSpace()
        {
            var zoom = float.IsFinite(Zoom) && Zoom > 0f ? Zoom : 1f;

            var radians = Rotation3D.ToRadians(RotationDegrees);

            return Matrix3x2.CreateScale(1f / PerspectiveScale)
                 * Matrix3x2.CreateTranslation(-Offset)
                 * Matrix3x2.CreateRotation(-radians)
                 * Matrix3x2.CreateScale(1f / zoom);
        }
    }
}
