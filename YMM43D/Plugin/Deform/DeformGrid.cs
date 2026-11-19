namespace YMM43D.Plugin
{
    public readonly record struct DeformGrid(int X, int Y, bool Separated)
    {
        public const int MinSegments = 1;
        public const int MaxSegments = 192;

        public static DeformGrid Create(int x, int y, bool separated = false)
            => new(Math.Clamp(x, MinSegments, MaxSegments), Math.Clamp(y, MinSegments, MaxSegments), separated);
    }
}
