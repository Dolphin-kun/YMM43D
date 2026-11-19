using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public readonly record struct SurfaceFace(int[] Indices, int Group, bool IsSmooth, Vector2[]? Uvs = null);
}
