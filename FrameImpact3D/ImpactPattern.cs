namespace FrameImpact3D
{
    internal readonly record struct ImpactPattern(ImpactKind Kind, int Count, int Seed)
    {
        public const int MostRings = 5;
        public const int FewestStrokes = 4;
        public const int MostStrokes = 1024;

        public const float RingStagger = 0.15f;
        public const float LineStartSpan = 0.2f;
        public const float LongestFlash = 2.8f;
    }
}
