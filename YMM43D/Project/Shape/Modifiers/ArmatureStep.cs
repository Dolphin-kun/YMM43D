using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record ArmatureStep(Matrix4x4[] Skin) : ModifierStep
    {
        public override MeshTopology Apply(MeshTopology mesh) => ShapeArmature.Deformed(mesh, Skin);

        public override bool IsQuick => true;

        public bool Equals(ArmatureStep? other) => other is not null && Skin.AsSpan().SequenceEqual(other.Skin);

        public override int GetHashCode() => Skin.Length;
    }
}
