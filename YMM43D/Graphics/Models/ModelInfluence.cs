using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelInfluence(ushort Bone0, ushort Bone1, ushort Bone2, ushort Bone3, Vector4 Weights)
    {
        public static ModelInfluence None => new(0, 0, 0, 0, Vector4.Zero);

        public bool IsEmpty => Weights == Vector4.Zero;
    }
}
