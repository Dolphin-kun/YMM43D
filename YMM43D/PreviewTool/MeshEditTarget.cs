using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    public enum MeshPart
    {
        Vertices,

        Edges,

        Faces,
    }

    internal readonly record struct MeshEditTarget(
        IVideoItem Item,
        Shape3DParameter Parameter,
        FrameContext Time,
        Matrix4x4 ToWorld,
        MeshTopology Mesh,
        Vector3[] Points)
    {
        public static MeshEditTarget? Resolve(IVideoItem item, in FrameContext time, in Matrix4x4 itemWorld)
        {
            if (item is not ShapeItem { ShapeParameter: Shape3DParameter parameter } || parameter.IsCustom)
                return null;

            var mesh = parameter.ShownMesh();

            if (mesh.Points.Length == 0 || mesh.Faces.Length == 0)
                return null;

            var toWorld = parameter.GetLocalMatrix(time) * itemWorld;
            var placed = new Vector3[mesh.Points.Length];

            for (var i = 0; i < placed.Length; i++)
                placed[i] = Vector3.Transform(mesh.Points[i], toWorld);

            return new MeshEditTarget(item, parameter, time, toWorld, mesh, placed);
        }

        public IEnumerable<int> VerticesOf(MeshPart part, int index)
        {
            switch (part)
            {
                case MeshPart.Edges when index >= 0 && index < Mesh.Edges.Length:
                    yield return Mesh.Edges[index].A;
                    yield return Mesh.Edges[index].B;
                    break;

                case MeshPart.Faces when index >= 0 && index < Mesh.Faces.Length:
                    foreach (var corner in Mesh.Faces[index])
                        yield return corner;

                    break;

                default:
                    if (index >= 0 && index < Mesh.Points.Length)
                        yield return index;

                    break;
            }
        }

        public IEnumerable<int> LoopOf(int edge)
        {
            foreach (var one in Mesh.EdgeLoop(edge))
            {
                yield return Mesh.Edges[one].A;
                yield return Mesh.Edges[one].B;
            }
        }

        public bool IsEdgeChosen(int edge, IReadOnlySet<int> selection)
            => selection.Contains(Mesh.Edges[edge].A) && selection.Contains(Mesh.Edges[edge].B);
    }
}
