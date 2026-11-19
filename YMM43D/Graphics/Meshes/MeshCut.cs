using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshCut
    {
        private const float Near = 1e-5f;

        public static (MeshTopology Mesh, int[] Added)? Connect(this MeshTopology mesh, IReadOnlyCollection<int> vertices)
        {
            var chosen = new HashSet<int>(mesh.ExistingPoints(vertices));
            var made = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();
            var changed = false;

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var ring = mesh.Faces[f];
                var marks = Enumerable.Range(0, ring.Length).Where(i => chosen.Contains(ring[i])).ToArray();
                var pieces = Pieces(ring, marks);

                if (pieces.Count < 2)
                {
                    made.Add(ring);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);
                    continue;
                }

                changed = true;

                foreach (var piece in pieces)
                {
                    made.Add(piece);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);
                }
            }

            return changed ? (mesh.With(mesh.Points, made, groups, smooth), [.. chosen]) : null;
        }

        public static (MeshTopology Mesh, int[] Added)? Bisect(this MeshTopology mesh, IReadOnlyCollection<int> faces, Vector3 point, Vector3 normal, bool clearsInner, bool clearsOuter, bool fills)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length == 0 || normal.LengthSquared() < 1e-12f)
                return null;

            normal = Vector3.Normalize(normal);

            var cutting = new Cutting(mesh);
            var cuts = new HashSet<int>();

            float Side(Vector3 place) => Vector3.Dot(place - point, normal);

            foreach (var edge in chosen.SelectMany(face => mesh.FaceEdges[face]).Distinct())
            {
                var (a, b) = (mesh.Edges[edge].A, mesh.Edges[edge].B);
                var (da, db) = (Side(mesh.Points[a]), Side(mesh.Points[b]));

                if (MathF.Abs(da) <= Near)
                    cuts.Add(a);

                if (MathF.Abs(db) <= Near)
                    cuts.Add(b);

                if (da * db >= 0f || MathF.Abs(da) <= Near || MathF.Abs(db) <= Near)
                    continue;

                var t = da / (da - db);

                cuts.Add(cutting.OnEdge(a, b, t));
            }

            if (cuts.Count == 0)
                return null;

            var chords = new List<(int A, int B)>();

            foreach (var face in chosen)
            {
                var ring = cutting.Faces[cutting.PieceOf(face)];
                var across = Vector3.Cross(normal, mesh.NormalOf(face));
                var crossing = new List<int>();

                for (var i = 0; i < ring.Length; i++)
                {
                    if (!cuts.Contains(ring[i]))
                        continue;

                    var before = Nonzero(ring, i, -1, cutting.Points, Side);
                    var after = Nonzero(ring, i, 1, cutting.Points, Side);

                    if (before * after < 0f)
                        crossing.Add(ring[i]);
                }

                crossing.Sort((x, y) => Vector3.Dot(cutting.Points[x], across).CompareTo(Vector3.Dot(cutting.Points[y], across)));

                for (var k = 0; k + 1 < crossing.Count; k += 2)
                {
                    var (a, b) = (crossing[k], crossing[k + 1]);

                    if (cutting.Split(face, a, b, [], (cutting.Points[a] + cutting.Points[b]) / 2f))
                        chords.Add((a, b));
                }
            }

            var dropped = new HashSet<int>();
            var origins = new HashSet<int>(chosen);

            if (clearsInner || clearsOuter)
            {
                for (var f = 0; f < cutting.Faces.Count; f++)
                {
                    if (!origins.Contains(cutting.Origin[f]))
                        continue;

                    var side = cutting.Faces[f].Sum(corner => Side(cutting.Points[corner]));

                    if ((clearsInner && side < 0f) || (clearsOuter && side > 0f))
                        dropped.Add(f);
                }
            }

            var (built, moved) = cutting.Build(dropped, mesh.IsIsolated);
            var kept = moved.Moved(cuts);

            if (!fills || dropped.Count == 0)
                return (built, kept);

            var chordEdges = chords
                .Select(chord => (moved[chord.A], moved[chord.B]))
                .Where(chord => chord.Item1 >= 0 && chord.Item2 >= 0)
                .Select(chord => built.FindEdge(chord.Item1, chord.Item2))
                .Where(edge => edge >= 0)
                .ToArray();

            foreach (var chain in built.Chains(chordEdges))
            {
                var ends = chain.SelectMany(edge => new[] { built.Edges[edge].A, built.Edges[edge].B }).Distinct().ToArray();
                var edges = ends.SelectMany(one => built.VertexEdges[one]).Where(edge => ends.Contains(built.Edges[edge].A) && ends.Contains(built.Edges[edge].B)).Distinct().ToArray();

                if (built.Fill(ends, edges) is { } filled)
                    built = filled.Mesh;
            }

            return (built, kept);
        }

        public static (MeshTopology Mesh, int[] Added)? Knife(this MeshTopology mesh, IReadOnlyList<IReadOnlyList<KnifePoint>> paths)
        {
            var cutting = new Cutting(mesh);
            var used = new HashSet<int>();

            foreach (var path in paths)
                Carve(mesh, cutting, path, used);

            if (used.Count == 0)
                return null;

            var (built, moved) = cutting.Build([], vertex => vertex < mesh.Points.Length && mesh.IsIsolated(vertex));

            return (built, moved.Moved(used));
        }

        private static List<int[]> Pieces(int[] ring, int[] marks)
        {
            if (marks.Length == 2)
            {
                var gap = marks[1] - marks[0];

                return gap == 1 || gap == ring.Length - 1 ? [] : [ring.Span(marks[0], marks[1]), ring.Span(marks[1], marks[0])];
            }

            if (marks.Length < 3)
                return [];

            var pieces = Enumerable.Range(0, marks.Length)
                .Select(k => ring.Span(marks[k], marks[(k + 1) % marks.Length]))
                .Where(piece => piece.Length >= 3)
                .ToList();

            if (pieces.Count == 0)
                return [];

            pieces.Add([.. marks.Select(i => ring[i])]);

            return pieces;
        }

        private static void Carve(MeshTopology mesh, Cutting cutting, IReadOnlyList<KnifePoint> path, HashSet<int> used)
        {
            var at = new int[path.Count];

            for (var i = 0; i < path.Count; i++)
            {
                var one = path[i];

                at[i] = one.Hit switch
                {
                    KnifeHit.Vertex => one.Index,
                    KnifeHit.Edge => cutting.OnEdge(mesh.Edges[one.Index].A, mesh.Edges[one.Index].B, Along(mesh, one.Index, one.Place)),
                    _ => cutting.AddPoint(one.Place),
                };
            }

            var start = -1;

            for (var i = 0; i < path.Count; i++)
            {
                if (path[i].Hit is KnifeHit.Face)
                    continue;

                if (start >= 0)
                {
                    var inner = Enumerable.Range(start + 1, i - start - 1).ToArray();
                    var faces = inner.Select(k => path[k].Index).Distinct().ToArray();
                    var (a, b) = (at[start], at[i]);
                    var hint = inner.Length > 0 ? path[inner[0]].Place : (path[start].Place + path[i].Place) / 2f;
                    var origin = faces.Length switch
                    {
                        0 => Shared(mesh, cutting.Points, path[start], path[i], hint),
                        1 when Touches(mesh, path[start], faces[0]) && Touches(mesh, path[i], faces[0]) => faces[0],
                        _ => -1,
                    };

                    if (origin >= 0 && a != b && cutting.Split(origin, a, b, [.. inner.Select(k => at[k])], hint))
                    {
                        used.Add(a);
                        used.Add(b);
                        used.UnionWith(inner.Select(k => at[k]));
                    }
                }

                start = i;
            }
        }

        private static float Along(MeshTopology mesh, int edge, Vector3 place)
        {
            var (a, b) = (mesh.Points[mesh.Edges[edge].A], mesh.Points[mesh.Edges[edge].B]);
            var span = b - a;

            return span.LengthSquared() < 1e-20f ? 0f : Math.Clamp(Vector3.Dot(place - a, span) / span.LengthSquared(), 0f, 1f);
        }

        private static bool Touches(MeshTopology mesh, KnifePoint point, int face) => point.Hit switch
        {
            KnifeHit.Vertex => mesh.Faces[face].Contains(point.Index),
            KnifeHit.Edge => mesh.FaceEdges[face].Contains(point.Index),
            _ => point.Index == face,
        };

        private static int Shared(MeshTopology mesh, List<Vector3> points, KnifePoint first, KnifePoint second, Vector3 hint)
        {
            IEnumerable<int> Around(KnifePoint point) => point.Hit is KnifeHit.Vertex
                ? mesh.VertexEdges[point.Index].SelectMany(edge => mesh.EdgeFaces[edge]).Distinct()
                : mesh.EdgeFaces[point.Index];

            var common = Around(first).Intersect(Around(second)).ToArray();

            return common.Length switch
            {
                0 => -1,
                1 => common[0],
                _ => common.FirstOrDefault(face => Holds(points, mesh.Faces[face], hint), common[0]),
            };
        }

        private static float Nonzero(int[] ring, int at, int step, List<Vector3> points, Func<Vector3, float> side)
        {
            for (var k = 1; k < ring.Length; k++)
            {
                var value = side(points[ring[((at + step * k) % ring.Length + ring.Length) % ring.Length]]);

                if (MathF.Abs(value) > Near)
                    return value;
            }

            return 0f;
        }

        private static bool Holds(List<Vector3> points, int[] ring, Vector3 place)
        {
            var normal = Vector3.Zero;

            for (var i = 0; i < ring.Length; i++)
                normal += Vector3.Cross(points[ring[i]], points[ring[(i + 1) % ring.Length]]);

            if (normal.LengthSquared() < 1e-20f)
                return false;

            normal = Vector3.Normalize(normal);

            var guess = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(guess, normal));
            var up = Vector3.Cross(normal, right);
            var (x, y) = (Vector3.Dot(place, right), Vector3.Dot(place, up));
            var inside = false;

            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var (xi, yi) = (Vector3.Dot(points[ring[i]], right), Vector3.Dot(points[ring[i]], up));
                var (xj, yj) = (Vector3.Dot(points[ring[j]], right), Vector3.Dot(points[ring[j]], up));

                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }

            return inside;
        }

        private sealed class Cutting(MeshTopology mesh)
        {
            private readonly Dictionary<(int, int), List<(float T, int Vertex)>> splits = [];
            private readonly List<int> groups = [.. mesh.Groups];
            private readonly List<bool> smooth = [.. mesh.Smooth];
            private readonly List<MeshEdge> loose = [.. mesh.LooseEdges];

            public List<Vector3> Points { get; } = [.. mesh.Points];

            public List<int[]> Faces { get; } = [.. mesh.Faces];

            public List<int> Origin { get; } = [.. Enumerable.Range(0, mesh.Faces.Length)];

            public int AddPoint(Vector3 place)
            {
                Points.Add(place);

                return Points.Count - 1;
            }

            public int OnEdge(int a, int b, float t)
            {
                if (a > b)
                    (a, b, t) = (b, a, 1f - t);

                if (!splits.TryGetValue((a, b), out var list))
                    splits[(a, b)] = list = [(0f, a), (1f, b)];

                var length = (mesh.Points[b] - mesh.Points[a]).Length();
                var at = list.FindIndex(one => one.T >= t);

                if (MathF.Abs(list[at].T - t) * length <= Near)
                    return list[at].Vertex;

                if (MathF.Abs(list[at - 1].T - t) * length <= Near)
                    return list[at - 1].Vertex;

                var (left, right) = (list[at - 1].Vertex, list[at].Vertex);
                var made = AddPoint(Vector3.Lerp(mesh.Points[a], mesh.Points[b], t));

                list.Insert(at, (t, made));

                for (var f = 0; f < Faces.Count; f++)
                {
                    var ring = Faces[f];

                    for (var i = 0; i < ring.Length; i++)
                    {
                        var next = ring[(i + 1) % ring.Length];

                        if ((ring[i] == left && next == right) || (ring[i] == right && next == left))
                        {
                            Faces[f] = [.. ring[..(i + 1)], made, .. ring[(i + 1)..]];
                            break;
                        }
                    }
                }

                for (var k = loose.Count - 1; k >= 0; k--)
                {
                    if (!loose[k].Holds(left) || !loose[k].Holds(right))
                        continue;

                    loose.RemoveAt(k);
                    loose.Add(new MeshEdge(left, made));
                    loose.Add(new MeshEdge(made, right));
                    break;
                }

                return made;
            }

            public int PieceOf(int origin) => Origin.IndexOf(origin);

            public bool Split(int origin, int a, int b, int[] inner, Vector3 hint)
            {
                var candidates = Enumerable.Range(0, Faces.Count)
                    .Where(f => Origin[f] == origin && Faces[f].Contains(a) && Faces[f].Contains(b))
                    .ToArray();

                if (candidates.Length == 0)
                    return false;

                var face = candidates.Length == 1 ? candidates[0] : candidates.FirstOrDefault(f => Holds(Points, Faces[f], hint), candidates[0]);
                var ring = Faces[face];
                var (ia, ib) = (Array.IndexOf(ring, a), Array.IndexOf(ring, b));
                var gap = ((ib - ia) % ring.Length + ring.Length) % ring.Length;

                if (inner.Length == 0 && (gap == 1 || gap == ring.Length - 1))
                    return false;

                int[] first = [.. ring.Span(ia, ib), .. inner.Reverse()];
                int[] second = [.. ring.Span(ib, ia), .. inner];

                Faces[face] = first;
                Faces.Add(second);
                Origin.Add(origin);
                groups.Add(groups[face]);
                smooth.Add(smooth[face]);

                return true;
            }

            public (MeshTopology Mesh, int[] Moved) Build(HashSet<int> dropped, Func<int, bool> keeps)
            {
                var faces = new List<int[]>();
                var keptGroups = new List<int>();
                var keptSmooth = new List<bool>();

                for (var f = 0; f < Faces.Count; f++)
                {
                    if (dropped.Contains(f))
                        continue;

                    faces.Add(Faces[f]);
                    keptGroups.Add(groups[f]);
                    keptSmooth.Add(smooth[f]);
                }

                return MeshPacking.Pack(Points, faces, keptGroups, keptSmooth, loose, keeps, mesh.GroupCount);
            }
        }
    }
}
