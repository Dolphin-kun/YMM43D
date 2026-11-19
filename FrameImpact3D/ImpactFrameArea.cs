namespace FrameImpact3D
{
    internal readonly record struct ImpactFrameArea(float Radius, float Softness)
    {
        public static ImpactFrameArea Whole => new(0f, 0f);

        public bool IsWhole => Radius <= 0f && Softness <= 0f;
    }
}
