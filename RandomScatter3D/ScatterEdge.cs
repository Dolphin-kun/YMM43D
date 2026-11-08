using System.Numerics;

namespace RandomScatter3D
{
    public static class ScatterEdge
    {
        public static float Alpha(in Vector3 position, in Vector3 velocity, in Vector3 range, float fade)
        {
            if (fade <= 0f)
                return 1f;

            return MathF.Min(
                AxisAlpha(position.X, velocity.X, range.X, fade),
                MathF.Min(
                    AxisAlpha(position.Y, velocity.Y, range.Y, fade),
                    AxisAlpha(position.Z, velocity.Z, range.Z, fade)));
        }

        private static float AxisAlpha(float position, float velocity, float span, float fade)
        {
            if (velocity == 0f || span <= 0f)
                return 1f;

            var half = span / 2f;
            var band = half * fade;

            if (band <= 0f)
                return 1f;

            return Math.Clamp((half - MathF.Abs(position)) / band, 0f, 1f);
        }
    }
}
