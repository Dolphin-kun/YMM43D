using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record ArrayStep(int Count, Vector3 Relative, Vector3 Constant, bool Merges, float Distance) : ModifierStep
    {
        public override MeshTopology Apply(MeshTopology mesh) => mesh.Arrayed(Count, Relative, Constant, Merges, Distance);
    }
}
