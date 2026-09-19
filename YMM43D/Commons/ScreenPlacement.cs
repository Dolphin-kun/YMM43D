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

        public const float FlatPerspectiveMargin = 4f;

        private const float MaxPerspectiveScale = 100f;

        public static float GetFlatPerspectiveDistance(float? hostDistance, float imageReach)
        {
            var current = hostDistance is { } distance && float.IsFinite(distance) && distance > 0f
                ? distance
                : HostPerspectiveDistance;

            if (!float.IsFinite(imageReach) || imageReach <= 0f)
                return current;

            return MathF.Max(current, imageReach * FlatPerspectiveMargin);
        }

        public static ScreenPlacement None => new(Vector2.Zero, 1f, 0f, 0f);

        // この配置の後に、親（グループ制御など）の配置が掛かるときの、合わせた配置。
        public ScreenPlacement Then(in ScreenPlacement parent)
        {
            var parentZoom = float.IsFinite(parent.Zoom) && parent.Zoom > 0f ? parent.Zoom : 1f;
            var turn = Matrix3x2.CreateRotation(float.DegreesToRadians(parent.RotationDegrees));

            return new ScreenPlacement(
                parent.Offset + Vector2.Transform(Offset * parentZoom, turn),
                Zoom * parentZoom,
                RotationDegrees + parent.RotationDegrees,
                parent.Depth + Depth * parentZoom);
        }

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

            var radians = float.DegreesToRadians(RotationDegrees);

            return Matrix3x2.CreateScale(1f / PerspectiveScale)
                 * Matrix3x2.CreateTranslation(-Offset)
                 * Matrix3x2.CreateRotation(-radians)
                 * Matrix3x2.CreateScale(1f / zoom);
        }
    }
}
