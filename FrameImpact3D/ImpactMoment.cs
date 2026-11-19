namespace FrameImpact3D
{
    internal readonly record struct ImpactMoment(float Progress, float Seconds, int Seed)
    {
        public bool IsStriking => Progress is >= 0f and < 1f;
    }
}
