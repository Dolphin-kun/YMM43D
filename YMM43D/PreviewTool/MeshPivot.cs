using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal static class MeshPivot
    {
        public static Vector3? Compute(MeshTopology mesh, in Matrix4x4 toWorld, MeshSelection selection, PivotPoint pivot, in Vector3 cursor)
        {
            if (selection.IsEmpty)
                return null;

            return pivot switch
            {
                PivotPoint.Cursor => cursor,
                PivotPoint.Bounds => Bounds(mesh, toWorld, selection.Vertices),
                PivotPoint.Active => ActiveCenter(mesh, toWorld, selection) ?? Median(mesh, toWorld, selection.Vertices),
                _ => Median(mesh, toWorld, selection.Vertices),
            };
        }

        public static Vector3 Median(MeshTopology mesh, in Matrix4x4 toWorld, IReadOnlyCollection<int> vertices)
        {
            var sum = Vector3.Zero;
            var count = 0;

            foreach (var vertex in vertices)
            {
                if (vertex < 0 || vertex >= mesh.Points.Length)
                    continue;

                sum += Vector3.Transform(mesh.Points[vertex], toWorld);
                count++;
            }

            return count == 0 ? Vector3.Transform(Vector3.Zero, toWorld) : sum / count;
        }

        public static Vector3 Bounds(MeshTopology mesh, in Matrix4x4 toWorld, IReadOnlyCollection<int> vertices)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var vertex in vertices)
            {
                if (vertex < 0 || vertex >= mesh.Points.Length)
                    continue;

                var placed = Vector3.Transform(mesh.Points[vertex], toWorld);

                min = Vector3.Min(min, placed);
                max = Vector3.Max(max, placed);
            }

            return min.X > max.X ? Vector3.Transform(Vector3.Zero, toWorld) : (min + max) / 2f;
        }

        public static Vector3? ActiveCenter(MeshTopology mesh, in Matrix4x4 toWorld, MeshSelection selection)
        {
            var active = selection.Active;

            if (active < 0)
                return null;

            return selection.Part switch
            {
                MeshPart.Faces when active < mesh.Faces.Length => Median(mesh, toWorld, mesh.Faces[active]),
                MeshPart.Edges when active < mesh.Edges.Length => Median(mesh, toWorld, [mesh.Edges[active].A, mesh.Edges[active].B]),
                MeshPart.Vertices when active < mesh.Points.Length => Vector3.Transform(mesh.Points[active], toWorld),
                _ => null,
            };
        }

        public static Dictionary<int, Vector3> Islands(MeshTopology mesh, in Matrix4x4 toWorld, MeshSelection selection)
        {
            var parent = new Dictionary<int, int>();

            foreach (var vertex in selection.Vertices)
                parent[vertex] = vertex;

            int Root(int vertex)
            {
                while (parent[vertex] != vertex)
                {
                    parent[vertex] = parent[parent[vertex]];
                    vertex = parent[vertex];
                }

                return vertex;
            }

            void Join(int a, int b)
            {
                if (!parent.ContainsKey(a) || !parent.ContainsKey(b))
                    return;

                var left = Root(a);
                var right = Root(b);

                if (left != right)
                    parent[left] = right;
            }

            if (selection.Part is MeshPart.Faces)
            {
                foreach (var face in selection.Faces)
                {
                    foreach (var edge in mesh.FaceEdges[face])
                    {
                        foreach (var other in mesh.EdgeFaces[edge])
                        {
                            if (other != face && selection.Faces.Contains(other))
                                Join(mesh.Edges[edge].A, mesh.Edges[edge].B);
                        }
                    }

                    var corners = mesh.Faces[face];

                    for (var i = 1; i < corners.Length; i++)
                        Join(corners[0], corners[i]);
                }
            }
            else
            {
                foreach (var edge in selection.Edges)
                    Join(mesh.Edges[edge].A, mesh.Edges[edge].B);
            }

            var sums = new Dictionary<int, (Vector3 Sum, int Count)>();

            foreach (var vertex in selection.Vertices)
            {
                var root = Root(vertex);
                var placed = Vector3.Transform(mesh.Points[vertex], toWorld);

                sums[root] = sums.TryGetValue(root, out var found) ? (found.Sum + placed, found.Count + 1) : (placed, 1);
            }

            var centers = new Dictionary<int, Vector3>();

            foreach (var vertex in selection.Vertices)
            {
                var (sum, count) = sums[Root(vertex)];

                centers[vertex] = sum / count;
            }

            return centers;
        }
    }
}
