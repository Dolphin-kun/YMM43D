using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct ShadowPlacement(float Strength, float Softness = 0f, int Slice = -1, float Radius = 0f, Matrix4x4 Matrix = default)
    {
        public const float SharpTexels = 1.5f;
        public const float SoftTexels = 12f;

        public bool IsWanted => Strength > 0f;

        public bool IsPlaced => Slice >= 0;

        public float RadiusFor(int resolution) => (SharpTexels + (SoftTexels - SharpTexels) * Softness) / Math.Max(resolution, 1);
    }
}
