using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelPart(
        int IndexStart,
        int IndexCount,
        Vector4 Color,
        int Image,
        int Material,
        ModelBlend Blend,
        float Cutoff,
        Vector3 Center,
        bool Unlit = false)
    {
        public ModelSurface Surface
        {
            get => field ?? ModelSurface.Default;
            init;
        }
    }
}
