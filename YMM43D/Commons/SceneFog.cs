using System.Numerics;

namespace YMM43D.Commons
{
    public readonly record struct SceneFog(Vector3 Color, float Density, float Start, float End)
    {
        public static SceneFog None => new(Vector3.Zero, 0f, 0f, 1f);

        public bool IsEnabled => Density > 0f && End > Start;
    }
}
