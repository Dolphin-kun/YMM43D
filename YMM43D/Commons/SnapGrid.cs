using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct SnapGrid(bool IsEnabled, float Step, float AngleStep)
    {
        public const float DefaultStep = 50f;

        public const float DefaultAngleStep = 15f;

        public static SnapGrid Off => new(false, DefaultStep, DefaultAngleStep);

        public SnapGrid Inverted(bool invert) => invert ? this with { IsEnabled = !IsEnabled } : this;

        public Vector3 SnapShift(in Vector3 startPosition, in Vector3 rawShift)
        {
            if (!IsEnabled || Step <= 0f)
                return rawShift;

            var start = WorldScale.ToPixelOffset(startPosition);
            var moved = start + WorldScale.ToPixelOffset(rawShift);

            var snapped = new Vector3(
                rawShift.X == 0f ? moved.X : Round(moved.X, Step),
                rawShift.Y == 0f ? moved.Y : Round(moved.Y, Step),
                rawShift.Z == 0f ? moved.Z : Round(moved.Z, Step));

            var pixels = snapped - start;

            return new Vector3(WorldScale.ToWorld(pixels.X), -WorldScale.ToWorld(pixels.Y), WorldScale.ToWorld(pixels.Z));
        }

        public float SnapTurn(float startDegrees, float rawTurn)
        {
            if (!IsEnabled || AngleStep <= 0f || rawTurn == 0f)
                return rawTurn;

            return Round(startDegrees + rawTurn, AngleStep) - startDegrees;
        }

        private static float Round(float value, float step) => MathF.Round(value / step) * step;
    }
}
