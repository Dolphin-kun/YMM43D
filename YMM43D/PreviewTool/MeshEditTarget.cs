using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
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

            if (mesh.Points.Length == 0)
                return null;

            var toWorld = parameter.GetLocalMatrix(time) * itemWorld;
            var placed = new Vector3[mesh.Points.Length];

            for (var i = 0; i < placed.Length; i++)
                placed[i] = Vector3.Transform(mesh.Points[i], toWorld);

            return new MeshEditTarget(item, parameter, time, toWorld, mesh, placed);
        }

        public int[] LoopOf(MeshPart part, int edge)
        {
            var mesh = Mesh;

            switch (part)
            {
                case MeshPart.Faces:
                    var ring = new HashSet<int>(mesh.EdgeRing(edge));

                    return [.. Enumerable.Range(0, mesh.Faces.Length).Where(face => mesh.FaceEdges[face].Count(ring.Contains) >= 2)];

                case MeshPart.Edges:
                    return mesh.EdgeLoop(edge);

                default:
                    return [.. mesh.EdgeLoop(edge).SelectMany(one => new[] { mesh.Edges[one].A, mesh.Edges[one].B }).Distinct()];
            }
        }
    }
}
