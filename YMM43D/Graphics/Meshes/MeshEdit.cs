using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshEdit
    {
        public static int[] FacesIn(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var found = new List<int>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (mesh.Faces[f].Length > 0 && mesh.Faces[f].All(selection.Contains))
                    found.Add(f);
            }

            return [.. found];
        }

        public static int[] Existing(this MeshTopology mesh, IEnumerable<int> faces)
            => [.. faces.Where(face => face >= 0 && face < mesh.Faces.Length).Distinct().Order()];

        public static int[] ExistingEdges(this MeshTopology mesh, IEnumerable<int> edges)
            => [.. edges.Where(edge => edge >= 0 && edge < mesh.Edges.Length).Distinct().Order()];

        public static int[] ExistingPoints(this MeshTopology mesh, IEnumerable<int> vertices)
            => [.. vertices.Where(vertex => vertex >= 0 && vertex < mesh.Points.Length).Distinct().Order()];

        public static (int[] Faces, int[] Edges, int[] Lone) Parted(this MeshTopology mesh, IReadOnlyCollection<int> faces, IReadOnlyCollection<int> edges, IReadOnlyCollection<int> vertices)
        {
            var chosenFaces = mesh.Existing(faces);
            var faceEdges = new HashSet<int>(chosenFaces.SelectMany(face => mesh.FaceEdges[face]));
            var chosenEdges = mesh.ExistingEdges(edges).Where(edge => !faceEdges.Contains(edge)).ToArray();
            var used = new HashSet<int>(chosenFaces.SelectMany(face => mesh.Faces[face]).Concat(chosenEdges.SelectMany(edge => new[] { mesh.Edges[edge].A, mesh.Edges[edge].B })));
            var lone = mesh.ExistingPoints(vertices).Where(vertex => !used.Contains(vertex)).ToArray();

            return (chosenFaces, chosenEdges, lone);
        }

        public static int[] LinkedTo(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var reached = new HashSet<int>(selection);
            var queue = new Queue<int>(selection);

            while (queue.Count > 0)
            {
                var at = queue.Dequeue();

                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                foreach (var edge in mesh.VertexEdges[at])
                {
                    var next = mesh.Edges[edge].Other(at);

                    if (reached.Add(next))
                        queue.Enqueue(next);
                }
            }

            return [.. reached];
        }

        public static int[] Grown(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var found = new HashSet<int>(selection);

            foreach (var at in selection)
            {
                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                foreach (var edge in mesh.VertexEdges[at])
                    found.Add(mesh.Edges[edge].Other(at));
            }

            return [.. found];
        }

        public static int[] Shrunk(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var found = new List<int>();

            foreach (var at in selection)
            {
                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                var inside = true;

                foreach (var edge in mesh.VertexEdges[at])
                {
                    if (!selection.Contains(mesh.Edges[edge].Other(at)))
                        inside = false;
                }

                if (inside)
                    found.Add(at);
            }

            return found.Count == 0 ? [.. selection] : [.. found];
        }

        public static (int Forward, int Backward)[] SlideRails(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var rails = new (int, int)[mesh.Points.Length];

            for (var i = 0; i < rails.Length; i++)
                rails[i] = (-1, -1);

            foreach (var at in selection)
            {
                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                var free = new List<int>();

                foreach (var edge in mesh.VertexEdges[at])
                {
                    var other = mesh.Edges[edge].Other(at);

                    if (!selection.Contains(other))
                        free.Add(other);
                }

                rails[at] = free.Count switch
                {
                    1 => (free[0], free[0]),
                    2 => (free[0], free[1]),
                    _ => (-1, -1),
                };
            }

            return rails;
        }

        public static (MeshTopology Mesh, int[] Added) Rebuilt(
            this MeshTopology mesh,
            IReadOnlyList<Vector3> points,
            IReadOnlyList<int[]> faces,
            IReadOnlyList<int> groups,
            IReadOnlyList<bool> smooth,
            IEnumerable<MeshEdge> loose,
            Func<int, bool> keeps,
            IEnumerable<int> chosen)
        {
            var (built, moved) = MeshPacking.Pack(points, faces, groups, smooth, loose, keeps, mesh.GroupCount);

            return (built, moved.Moved(chosen));
        }
    }
}
