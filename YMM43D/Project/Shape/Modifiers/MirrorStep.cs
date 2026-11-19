using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record MirrorStep(bool X, bool Y, bool Z, bool Bisects, float Merge) : ModifierStep
    {
        public override MeshTopology Apply(MeshTopology mesh) => mesh.Mirrored(X, Y, Z, Bisects, Merge);
    }
}
