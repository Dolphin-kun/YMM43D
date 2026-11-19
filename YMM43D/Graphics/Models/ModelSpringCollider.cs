using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelSpringCollider(int Bone, Vector3 Offset, float Radius, Vector3 Tail, bool IsCapsule);
}
