using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshPacking
    {
        public static (MeshTopology Mesh, int[] Moved) Pack(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<int[]> faces,
            IReadOnlyList<int> groups,
            IReadOnlyList<bool> smooth,
            IEnumerable<MeshEdge> loose,
            Func<int, bool> keeps,
            int groupCount)
        {
            var used = new bool[points.Count];
            var edges = loose.Where(edge => edge.A >= 0 && edge.B >= 0 && edge.A < points.Count && edge.B < points.Count && edge.A != edge.B).ToArray();

            foreach (var face in faces)
            {
                foreach (var corner in face)
                    used[corner] = true;
            }

            foreach (var edge in edges)
            {
                used[edge.A] = true;
                used[edge.B] = true;
            }

            var moved = new int[points.Count];
            var kept = new List<Vector3>();

            for (var i = 0; i < points.Count; i++)
            {
                if (!used[i] && !keeps(i))
                {
                    moved[i] = -1;
                    continue;
                }

                moved[i] = kept.Count;
                kept.Add(points[i]);
            }

            var mesh = new MeshTopology(
                [.. kept],
                [.. faces.Select(face => face.Select(corner => moved[corner]).ToArray())],
                [.. groups],
                [.. smooth],
                groupCount,
                edges.Select(edge => new MeshEdge(moved[edge.A], moved[edge.B])));

            return (mesh, moved);
        }

        public static bool IsIsolated(this MeshTopology mesh, int vertex) => vertex >= 0 && vertex < mesh.Points.Length && mesh.VertexEdges[vertex].Length == 0;

        public static int[] Moved(this int[] moved, IEnumerable<int> vertices)
            => [.. vertices.Where(one => one >= 0 && one < moved.Length && moved[one] >= 0).Select(one => moved[one]).Distinct()];
    }
}
