using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelBone(string Name, int Parent, Matrix4x4 Local, Matrix4x4 InverseBind);
}
