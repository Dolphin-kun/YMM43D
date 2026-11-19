using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record BooleanTarget(MeshTopology Mesh, Matrix4x4 ToLocal);
}
