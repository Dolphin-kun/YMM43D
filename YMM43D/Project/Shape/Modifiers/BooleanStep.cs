using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape.Modifiers
{
    internal sealed record BooleanStep(BooleanOperation Operation, MeshTopology Target, Matrix4x4 ToLocal) : ModifierStep
    {
        public override MeshTopology Apply(MeshTopology mesh)
        {
            var transform = ToLocal;
            var placed = Target.WithPoints([.. Target.Points.Select(point => Vector3.Transform(point, transform))]);

            if (transform.GetDeterminant() < 0f)
                placed = placed.With(placed.Points, placed.Faces.Select(face => face.Reverse().ToArray()), placed.Groups, placed.Smooth);

            return mesh.Combined(placed, Operation);
        }
    }
}
