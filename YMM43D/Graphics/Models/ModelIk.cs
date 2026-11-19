namespace YMM43D.Graphics.Models
{
    public sealed record ModelIk(int Bone, int Target, int Loops, float Limit, ModelIkLink[] Links);
}
