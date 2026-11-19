using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record SolidifyStep(float Thickness) : ModifierStep
    {
        public override MeshTopology Apply(MeshTopology mesh) => mesh.Solidify([], Thickness) is { } made ? made.Mesh.Carry(mesh) : mesh;
    }
}
