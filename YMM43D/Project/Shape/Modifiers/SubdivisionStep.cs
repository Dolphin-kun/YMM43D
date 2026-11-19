using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record SubdivisionStep(int Levels) : ModifierStep
    {
        public override MeshTopology Apply(MeshTopology mesh) => mesh.CatmullClark(Levels);
    }
}
