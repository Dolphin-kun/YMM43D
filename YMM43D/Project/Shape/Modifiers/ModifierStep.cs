using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal abstract record ModifierStep
    {
        public abstract MeshTopology Apply(MeshTopology mesh);

        public virtual bool IsQuick => false;
    }
}
