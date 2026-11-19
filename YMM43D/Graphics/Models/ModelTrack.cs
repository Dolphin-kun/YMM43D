namespace YMM43D.Graphics.Models
{
    public sealed record ModelTrack(int Bone, int Morph, ModelTrackKind Kind, float[] Times, float[] Values, bool Stepped);
}
