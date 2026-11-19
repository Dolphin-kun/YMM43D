namespace YMM43D.Graphics.Models
{
    public sealed record ModelAnimation(string Name, float Duration, ModelTrack[] Tracks, ModelAnimatedBone[] Bones)
    {
        public bool Relative { get; init; }
    }
}
