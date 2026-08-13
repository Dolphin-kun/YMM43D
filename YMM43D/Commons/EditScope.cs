using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public readonly record struct EditScope(bool InsertsKeyFrame, int Frame)
    {
        public static EditScope Whole => default;

        public static EditScope AtFrame(int frame) => new(true, Math.Max(0, frame));

        public void Nudge(Animation animation, double delta)
        {
            if (InsertsKeyFrame)
                animation.NudgeAt(delta, Frame);
            else
                animation.Nudge(delta);
        }
    }
}
