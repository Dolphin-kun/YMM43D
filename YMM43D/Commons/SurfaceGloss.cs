namespace YMM43D.Commons
{
    public readonly record struct SurfaceGloss(float Strength, float Power)
    {
        public const float MinPower = 2f;

        public const float MaxPower = 1024f;

        public static SurfaceGloss None => default;

        public bool IsVisible => Strength > 0f;

        public static SurfaceGloss FromPercent(float strength, float sharpness)
        {
            var amount = Math.Clamp(strength / 100f, 0f, 10f);
            var rate = Math.Clamp(sharpness / 100f, 0f, 1f);

            return new SurfaceGloss(amount, MinPower * MathF.Pow(MaxPower / MinPower, rate));
        }
    }
}
