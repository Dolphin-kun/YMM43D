namespace YMM43D.Graphics.Meshes
{
    public static class MeshRegions
    {
        public static List<int[]>? BoundaryLoops(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var directed = new Dictionary<(int, int), int>();

            foreach (var f in faces)
            {
                var corners = mesh.Faces[f];

                for (var i = 0; i < corners.Length; i++)
                {
                    var key = (corners[i], corners[(i + 1) % corners.Length]);

                    directed[key] = directed.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }

            var next = new Dictionary<int, int>();

            foreach (var ((a, b), count) in directed)
            {
                if (count > 1)
                    return null;

                if (directed.ContainsKey((b, a)))
                    continue;

                if (!next.TryAdd(a, b))
                    return null;
            }

            var loops = new List<int[]>();
            var used = new HashSet<int>();

            foreach (var start in next.Keys)
            {
                if (used.Contains(start))
                    continue;

                var loop = new List<int>();
                var at = start;

                while (used.Add(at))
                {
                    loop.Add(at);

                    if (!next.TryGetValue(at, out at))
                        return null;
                }

                if (at != start || loop.Count < 3)
                    return null;

                loops.Add([.. loop]);
            }

            return loops;
        }

        public static List<List<int>> Islands(this MeshTopology mesh, IReadOnlyCollection<int> faces, Func<int, bool>? joinsThrough = null)
        {
            var chosen = new HashSet<int>(faces);
            var seen = new HashSet<int>();
            var islands = new List<List<int>>();

            foreach (var start in chosen)
            {
                if (!seen.Add(start))
                    continue;

                var island = new List<int>();
                var queue = new Queue<int>();

                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var face = queue.Dequeue();

                    island.Add(face);

                    foreach (var edge in mesh.FaceEdges[face])
                    {
                        if (joinsThrough is not null && !joinsThrough(edge))
                            continue;

                        foreach (var other in mesh.EdgeFaces[edge])
                        {
                            if (chosen.Contains(other) && seen.Add(other))
                                queue.Enqueue(other);
                        }
                    }
                }

                islands.Add(island);
            }

            return islands;
        }

        public static List<List<int>> Chains(this MeshTopology mesh, IReadOnlyCollection<int> edges)
        {
            var chosen = new HashSet<int>(edges);
            var seen = new HashSet<int>();
            var chains = new List<List<int>>();

            foreach (var start in chosen)
            {
                if (seen.Contains(start))
                    continue;

                var group = new List<int>();
                var queue = new Queue<int>();

                queue.Enqueue(start);
                seen.Add(start);

                while (queue.Count > 0)
                {
                    var edge = queue.Dequeue();

                    group.Add(edge);

                    foreach (var end in new[] { mesh.Edges[edge].A, mesh.Edges[edge].B })
                    {
                        foreach (var other in mesh.VertexEdges[end])
                        {
                            if (chosen.Contains(other) && seen.Add(other))
                                queue.Enqueue(other);
                        }
                    }
                }

                chains.Add(group);
            }

            return chains;
        }

        public static (int[] Vertices, bool Closed)? Ordered(this MeshTopology mesh, IReadOnlyCollection<int> edges)
        {
            if (edges.Count == 0)
                return null;

            var around = new Dictionary<int, List<int>>();

            foreach (var edge in edges)
            {
                var (a, b) = (mesh.Edges[edge].A, mesh.Edges[edge].B);

                if (!around.TryGetValue(a, out var fromA))
                    around[a] = fromA = [];

                if (!around.TryGetValue(b, out var fromB))
                    around[b] = fromB = [];

                fromA.Add(b);
                fromB.Add(a);
            }

            if (around.Values.Any(list => list.Count > 2))
                return null;

            var ends = around.Where(pair => pair.Value.Count == 1).Select(pair => pair.Key).ToArray();

            if (ends.Length is not (0 or 2))
                return null;

            var start = ends.Length == 2 ? ends[0] : around.Keys.First();
            var order = new List<int> { start };
            var previous = -1;
            var at = start;

            while (true)
            {
                var step = around[at].FirstOrDefault(one => one != previous && !order.Contains(one), -1);

                if (step < 0)
                    break;

                order.Add(step);
                previous = at;
                at = step;
            }

            return order.Count == around.Count ? ([.. order], ends.Length == 0) : null;
        }

        public static bool Runs(this int[] face, int a, int b)
        {
            for (var i = 0; i < face.Length; i++)
            {
                if (face[i] == a && face[(i + 1) % face.Length] == b)
                    return true;
            }

            return false;
        }

        public static int[] Span(this int[] ring, int from, int to)
        {
            var span = new List<int>();

            for (var i = from; ; i = (i + 1) % ring.Length)
            {
                span.Add(ring[i]);

                if (i == to)
                    break;
            }

            return [.. span];
        }

        public static int[] Oriented(this MeshTopology mesh, int[] loop)
        {
            for (var i = 0; i < loop.Length; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Length];
                var edge = mesh.FindEdge(a, b);

                if (edge < 0 || mesh.EdgeFaces[edge].Length != 1)
                    continue;

                return mesh.Faces[mesh.EdgeFaces[edge][0]].Runs(a, b) ? [.. loop.Reverse()] : loop;
            }

            return loop;
        }
    }
}
