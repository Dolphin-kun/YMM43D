using System.Numerics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public readonly record struct EditScope(bool InsertsKeyFrame, int Frame, int Length = 0, int Fps = 0)
    {
        public static EditScope Whole => default;

        public static EditScope AtFrame(int frame) => new(true, Math.Max(0, frame));

        public static EditScope InSegment(in FrameContext time)
            => new(false, Math.Max(0, time.Frame), Math.Max(0, time.Length), Math.Max(0, time.Fps));

        public void Nudge(Animation animation, double delta)
        {
            if (InsertsKeyFrame)
                animation.NudgeAt(delta, Frame);
            else if (Length > 0 && Fps > 0)
                animation.NudgeSegment(delta, new FrameContext(Frame, Length, Fps));
            else
                animation.Nudge(delta);
        }

        public void NudgePosition(Animation x, Animation y, Animation z, in Vector3 worldShift)
        {
            var pixels = WorldScale.ToPixelOffset(worldShift);

            Nudge(x, pixels.X);
            Nudge(y, pixels.Y);
            Nudge(z, pixels.Z);
        }
    }
}
