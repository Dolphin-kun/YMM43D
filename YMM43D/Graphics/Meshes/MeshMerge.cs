using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshMerge
    {
        public static (MeshTopology Mesh, int[] Added)? MergeAt(this MeshTopology mesh, IReadOnlyCollection<int> vertices, Vector3 place)
        {
            var chosen = mesh.ExistingPoints(vertices);

            if (chosen.Length < 2)
                return null;

            var into = new int[mesh.Points.Length];

            for (var i = 0; i < into.Length; i++)
                into[i] = i;

            foreach (var one in chosen)
                into[one] = chosen[0];

            return Welded(mesh, into, new Dictionary<int, Vector3> { [chosen[0]] = place }, [chosen[0]]);
        }

        public static (MeshTopology Mesh, int[] Added)? Collapse(this MeshTopology mesh, IReadOnlyCollection<int> vertices)
        {
            var chosen = new HashSet<int>(mesh.ExistingPoints(vertices));

            if (chosen.Count < 2)
                return null;

            var into = new int[mesh.Points.Length];
            var places = new Dictionary<int, Vector3>();

            for (var i = 0; i < into.Length; i++)
                into[i] = i;

            var seen = new HashSet<int>();

            foreach (var start in chosen.Order())
            {
                if (!seen.Add(start))
                    continue;

                var part = new List<int> { start };
                var queue = new Queue<int>(part);

                while (queue.Count > 0)
                {
                    var at = queue.Dequeue();

                    foreach (var edge in mesh.VertexEdges[at])
                    {
                        var next = mesh.Edges[edge].Other(at);

                        if (chosen.Contains(next) && seen.Add(next))
                        {
                            part.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }

                if (part.Count < 2)
                    continue;

                foreach (var one in part)
                    into[one] = start;

                places[start] = mesh.Middle(part);
            }

            return places.Count == 0 ? null : Welded(mesh, into, places, chosen.Select(one => into[one]));
        }

        public static (MeshTopology Mesh, int[] Added, int Removed)? MergeByDistance(this MeshTopology mesh, IReadOnlyCollection<int> vertices, float distance)
        {
            var chosen = mesh.ExistingPoints(vertices);

            if (chosen.Length < 2)
                return null;

            var reach = MathF.Max(distance, 1e-6f);
            var cells = new Dictionary<(int, int, int), List<int>>();
            var owner = new int[mesh.Points.Length];

            for (var i = 0; i < owner.Length; i++)
                owner[i] = i;

            int Root(int one)
            {
                while (owner[one] != one)
                    one = owner[one] = owner[owner[one]];

                return one;
            }

            (int, int, int) CellOf(Vector3 point) => ((int)MathF.Floor(point.X / reach), (int)MathF.Floor(point.Y / reach), (int)MathF.Floor(point.Z / reach));

            foreach (var one in chosen)
            {
                var point = mesh.Points[one];
                var (x, y, z) = CellOf(point);

                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            if (!cells.TryGetValue((x + dx, y + dy, z + dz), out var near))
                                continue;

                            foreach (var other in near)
                            {
                                if (Vector3.DistanceSquared(point, mesh.Points[other]) > reach * reach)
                                    continue;

                                var (a, b) = (Root(one), Root(other));

                                if (a != b)
                                    owner[Math.Max(a, b)] = Math.Min(a, b);
                            }
                        }
                    }
                }

                if (!cells.TryGetValue((x, y, z), out var list))
                    cells[(x, y, z)] = list = [];

                list.Add(one);
            }

            var into = new int[mesh.Points.Length];
            var removed = 0;

            for (var i = 0; i < into.Length; i++)
            {
                into[i] = Root(i);

                if (into[i] != i)
                    removed++;
            }

            if (removed == 0)
                return (mesh, chosen, 0);

            return Welded(mesh, into, [], chosen.Select(one => into[one])) is { } made ? (made.Mesh, made.Added, removed) : null;
        }

        public static (MeshTopology Mesh, int[] Added)? MergeInto(this MeshTopology mesh, IReadOnlyDictionary<int, int> pairs, IEnumerable<int> chosen)
        {
            var into = new int[mesh.Points.Length];

            for (var i = 0; i < into.Length; i++)
                into[i] = pairs.TryGetValue(i, out var target) ? target : i;

            return Welded(mesh, into, [], chosen.Select(one => into[one]));
        }

        private static (MeshTopology Mesh, int[] Added)? Welded(MeshTopology mesh, int[] into, Dictionary<int, Vector3> places, IEnumerable<int> chosen)
        {
            var points = (Vector3[])mesh.Points.Clone();

            foreach (var (one, place) in places)
                points[one] = place;

            var found = new List<(int[] Corners, int Group, bool Smooth)>();
            var leftover = new List<MeshEdge>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f].Select(corner => into[corner]).ToList();

                for (var i = corners.Count - 1; i >= 0 && corners.Count > 1; i--)
                {
                    if (corners[i] == corners[(i + 1) % corners.Count])
                        corners.RemoveAt(i);
                }

                foreach (var loop in Split(corners))
                {
                    if (loop.Count == 2)
                    {
                        leftover.Add(new MeshEdge(loop[0], loop[1]));
                        continue;
                    }

                    if (loop.Count >= 3)
                        found.Add(([.. loop], mesh.Groups[f], mesh.Smooth[f]));
                }
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            foreach (var same in found.GroupBy(face => string.Join(',', face.Corners.Order())))
            {
                var (corners, group, flat) = same.First();

                if (same.Skip(1).Any(other => Opposes(corners, other.Corners)))
                    continue;

                faces.Add(corners);
                groups.Add(group);
                smooth.Add(flat);
            }

            var loose = mesh.LooseEdges
                .Select(edge => new MeshEdge(into[edge.A], into[edge.B]))
                .Concat(leftover)
                .Where(edge => edge.A != edge.B);
            var merged = into.Where((target, one) => target != one).ToHashSet();
            var (built, moved) = MeshPacking.Pack(points, faces, groups, smooth, loose, vertex => into[vertex] == vertex && (mesh.IsIsolated(vertex) || merged.Contains(vertex)), mesh.GroupCount);

            return (built, moved.Moved(chosen));
        }

        private static bool Opposes(int[] first, int[] second) => first.Length >= 2 && second.Runs(first[1], first[0]);

        private static List<List<int>> Split(List<int> corners)
        {
            var loops = new List<List<int>>();
            var path = new List<int>();
            var at = new Dictionary<int, int>();

            foreach (var corner in corners)
            {
                if (at.TryGetValue(corner, out var start))
                {
                    var loop = path.GetRange(start, path.Count - start);

                    loops.Add(loop);
                    path.RemoveRange(start + 1, path.Count - start - 1);

                    foreach (var one in loop.Skip(1))
                        at.Remove(one);

                    continue;
                }

                at[corner] = path.Count;
                path.Add(corner);
            }

            loops.Add(path);

            return loops;
        }
    }
}
