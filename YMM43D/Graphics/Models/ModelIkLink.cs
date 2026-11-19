using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelIkLink(int Bone, bool Limited, Vector3 Lower, Vector3 Upper);
}
