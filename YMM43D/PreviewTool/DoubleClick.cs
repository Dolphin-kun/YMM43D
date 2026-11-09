using System.Numerics;

namespace YMM43D.PreviewTool
{
    public struct DoubleClick
    {
        public const long Milliseconds = 400;

        public const float Slop = 4f;

        private long lastTick;
        private Vector2 lastAt;

        public bool Twice(long now, in Vector2 at)
        {
            var twice = now - lastTick <= Milliseconds && Vector2.Distance(lastAt, at) <= Slop;

            lastTick = twice ? 0 : now;
            lastAt = at;

            return twice;
        }
    }
}
