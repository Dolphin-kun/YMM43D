using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshSubdivide
    {
        public static (MeshTopology Mesh, int[] Added)? Subdivide(this MeshTopology mesh, IReadOnlyCollection<int> edges, int cuts)
        {
            var chosen = new HashSet<int>(mesh.ExistingEdges(edges));
            var count = Math.Max(1, cuts);

            if (chosen.Count == 0)
                return null;

            var points = new List<Vector3>(mesh.Points);
            var along = new Dictionary<int, int[]>();

            foreach (var edge in chosen.Order())
            {
                var (a, b) = (mesh.Points[mesh.Edges[edge].A], mesh.Points[mesh.Edges[edge].B]);
                var made = new int[count];

                for (var t = 0; t < count; t++)
                {
                    made[t] = points.Count;
                    points.Add(Vector3.Lerp(a, b, (t + 1f) / (count + 1f)));
                }

                along[edge] = made;
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();
            var selected = new HashSet<int>();

            foreach (var edge in chosen)
            {
                selected.Add(mesh.Edges[edge].A);
                selected.Add(mesh.Edges[edge].B);
            }

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f];
                var sides = new int[]?[corners.Length];
                var hit = new List<int>();

                for (var i = 0; i < corners.Length; i++)
                {
                    var edge = mesh.FaceEdges[f][i];

                    if (!along.TryGetValue(edge, out var made))
                        continue;

                    sides[i] = mesh.Edges[edge].A == corners[i] ? made : [.. made.Reverse()];
                    hit.Add(i);
                }

                var pieces = hit.Count == 0 ? [corners] : Pieces(points, corners, sides, hit, count);

                foreach (var piece in pieces)
                {
                    faces.Add(piece);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);
                }
            }

            var loose = new List<MeshEdge>();

            foreach (var edge in mesh.LooseEdges)
            {
                var index = mesh.FindEdge(edge.A, edge.B);

                if (!along.TryGetValue(index, out var made))
                {
                    loose.Add(edge);
                    continue;
                }

                int[] chain = [mesh.Edges[index].A, .. made, mesh.Edges[index].B];

                for (var i = 0; i + 1 < chain.Length; i++)
                    loose.Add(new MeshEdge(chain[i], chain[i + 1]));
            }

            var result = new MeshTopology([.. points], [.. faces], [.. groups], [.. smooth], mesh.GroupCount, loose);

            return (result, [.. selected, .. Enumerable.Range(mesh.Points.Length, points.Count - mesh.Points.Length)]);
        }

        private static List<int[]> Pieces(List<Vector3> points, int[] corners, int[]?[] sides, List<int> hit, int count)
        {
            var n = corners.Length;

            if (n == 4 && hit.Count == 4)
                return Grid(points, corners, sides, count);

            if (n == 3 && hit.Count == 3)
                return Triangles(points, corners, sides, count);

            if (n == 4 && hit.Count == 2 && hit[1] - hit[0] == 2)
                return Strips(Turned(corners, hit[0]), Turned(sides, hit[0]));

            if (n == 4 && hit.Count == 2 && (hit[1] - hit[0] == 1 || (hit[0] == 0 && hit[1] == 3)))
            {
                var start = hit[1] - hit[0] == 1 ? hit[0] : 3;

                return Corner(Turned(corners, start), Turned(sides, start));
            }

            return [Ringed(corners, sides)];
        }

        private static T[] Turned<T>(T[] values, int start) => [.. Enumerable.Range(0, values.Length).Select(i => values[(start + i) % values.Length])];

        private static int[] Ringed(int[] corners, int[]?[] sides)
        {
            var ring = new List<int>();

            for (var i = 0; i < corners.Length; i++)
            {
                ring.Add(corners[i]);

                if (sides[i] is { } made)
                    ring.AddRange(made);
            }

            return [.. ring];
        }

        private static List<int[]> Grid(List<Vector3> points, int[] corners, int[]?[] sides, int count)
        {
            var n = count + 1;
            var grid = new int[n + 1, n + 1];

            grid[0, 0] = corners[0];
            grid[n, 0] = corners[1];
            grid[n, n] = corners[2];
            grid[0, n] = corners[3];

            for (var t = 1; t < n; t++)
            {
                grid[t, 0] = sides[0]![t - 1];
                grid[n, t] = sides[1]![t - 1];
                grid[n - t, n] = sides[2]![t - 1];
                grid[0, n - t] = sides[3]![t - 1];
            }

            for (var u = 1; u < n; u++)
            {
                for (var v = 1; v < n; v++)
                {
                    var (s, r) = ((float)u / n, (float)v / n);
                    var place = (1f - r) * points[grid[u, 0]] + r * points[grid[u, n]] + (1f - s) * points[grid[0, v]] + s * points[grid[n, v]]
                        - ((1f - s) * (1f - r) * points[grid[0, 0]] + s * (1f - r) * points[grid[n, 0]] + (1f - s) * r * points[grid[0, n]] + s * r * points[grid[n, n]]);

                    grid[u, v] = points.Count;
                    points.Add(place);
                }
            }

            var faces = new List<int[]>();

            for (var u = 0; u < n; u++)
            {
                for (var v = 0; v < n; v++)
                    faces.Add([grid[u, v], grid[u + 1, v], grid[u + 1, v + 1], grid[u, v + 1]]);
            }

            return faces;
        }

        private static List<int[]> Triangles(List<Vector3> points, int[] corners, int[]?[] sides, int count)
        {
            var n = count + 1;
            var grid = new Dictionary<(int, int), int>
            {
                [(0, 0)] = corners[0],
                [(n, 0)] = corners[1],
                [(0, n)] = corners[2],
            };

            for (var t = 1; t < n; t++)
            {
                grid[(t, 0)] = sides[0]![t - 1];
                grid[(n - t, t)] = sides[1]![t - 1];
                grid[(0, n - t)] = sides[2]![t - 1];
            }

            var (a, b, c) = (points[corners[0]], points[corners[1]], points[corners[2]]);

            for (var i = 1; i < n; i++)
            {
                for (var j = 1; i + j < n; j++)
                {
                    grid[(i, j)] = points.Count;
                    points.Add(a + (b - a) * i / n + (c - a) * j / n);
                }
            }

            var faces = new List<int[]>();

            for (var i = 0; i < n; i++)
            {
                for (var j = 0; i + j < n; j++)
                {
                    faces.Add([grid[(i, j)], grid[(i + 1, j)], grid[(i, j + 1)]]);

                    if (i + j < n - 1)
                        faces.Add([grid[(i + 1, j)], grid[(i + 1, j + 1)], grid[(i, j + 1)]]);
                }
            }

            return faces;
        }

        private static List<int[]> Strips(int[] corners, int[]?[] sides)
        {
            int[] left = [corners[0], .. sides[0]!, corners[1]];
            int[] right = [corners[3], .. sides[2]!.Reverse(), corners[2]];
            var faces = new List<int[]>();

            for (var t = 0; t + 1 < left.Length; t++)
                faces.Add([left[t], left[t + 1], right[t + 1], right[t]]);

            return faces;
        }

        private static List<int[]> Corner(int[] corners, int[]?[] sides)
        {
            var first = sides[0]!;
            var second = sides[1]!;
            var k = first.Length;
            var faces = new List<int[]> { new[] { first[k - 1], corners[1], second[0] } };

            for (var j = 1; j < k; j++)
                faces.Add([first[k - j - 1], first[k - j], second[j - 1], second[j]]);

            faces.Add([corners[0], first[0], second[k - 1], corners[2], corners[3]]);

            return faces;
        }
    }
}
