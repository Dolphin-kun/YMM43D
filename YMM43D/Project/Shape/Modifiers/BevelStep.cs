using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record BevelStep(float Amount, int Segments, float AngleLimit) : ModifierStep
    {
        public override MeshTopology Apply(MeshTopology mesh) => mesh.Beveled(Amount, Segments, AngleLimit);
    }
}
