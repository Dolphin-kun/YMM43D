using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshFaces
    {
        private const float QuadLimit = 40f * MathF.PI / 180f;

        public static (MeshTopology Mesh, int[] Added)? Triangulate(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var chosen = new HashSet<int>(mesh.Existing(faces).Where(face => mesh.Faces[face].Length > 3));

            if (chosen.Count == 0)
                return null;

            var made = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();
            var uvs = mesh.Uvs is null ? null : new List<Vector2[]>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var pieces = chosen.Contains(f) ? Ears(mesh, f) : [mesh.Faces[f]];

                foreach (var piece in pieces)
                {
                    made.Add(piece);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);
                    uvs?.Add([.. piece.Select(corner => mesh.Uvs![f][Array.IndexOf(mesh.Faces[f], corner)])]);
                }
            }

            var result = mesh.With(mesh.Points, made, groups, smooth);

            return (uvs is null ? result : result.WithUvs([.. uvs]), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        public static (MeshTopology Mesh, int[] Added)? TrianglesToQuads(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var chosen = new HashSet<int>(mesh.Existing(faces).Where(face => mesh.Faces[face].Length == 3));
            var pairs = new List<(float Score, int Edge, int First, int Second)>();

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                if (mesh.EdgeFaces[e] is not [var first, var second] || !chosen.Contains(first) || !chosen.Contains(second))
                    continue;

                if (Joined(mesh, e, first, second) is not { } quad)
                    continue;

                var bend = Angle(mesh.NormalOf(first), mesh.NormalOf(second));
                var shape = Shape(mesh, quad);

                if (bend > QuadLimit || shape > QuadLimit)
                    continue;

                pairs.Add((bend + shape, e, first, second));
            }

            if (pairs.Count == 0)
                return null;

            var used = new HashSet<int>();
            var quads = new Dictionary<int, int[]>();
            var dropped = new HashSet<int>();

            foreach (var (_, edge, first, second) in pairs.OrderBy(pair => pair.Score))
            {
                if (used.Contains(first) || used.Contains(second))
                    continue;

                used.Add(first);
                used.Add(second);
                quads[first] = Joined(mesh, edge, first, second)!;
                dropped.Add(second);
            }

            var made = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (dropped.Contains(f))
                    continue;

                made.Add(quads.TryGetValue(f, out var quad) ? quad : mesh.Faces[f]);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            return (mesh.With(mesh.Points, made, groups, smooth), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        public static (MeshTopology Mesh, int[] Added)? RotateEdges(this MeshTopology mesh, IReadOnlyCollection<int> edges, bool backward)
        {
            var faces = mesh.Faces.Select(face => face).ToArray();
            var touched = new HashSet<int>();
            var turned = new List<int>();

            foreach (var edge in mesh.ExistingEdges(edges))
            {
                if (mesh.EdgeFaces[edge] is not [var first, var second] || touched.Contains(first) || touched.Contains(second))
                    continue;

                var (a, b) = (mesh.Edges[edge].A, mesh.Edges[edge].B);

                if (!faces[first].Runs(a, b))
                    (a, b) = (b, a);

                if (!faces[first].Runs(a, b) || !faces[second].Runs(b, a))
                    continue;

                var near = From(faces[first], b);
                var far = From(faces[second], a);
                int[] loop = [.. near, .. far[1..^1]];
                var m = near.Length - 2;
                var n = far.Length - 2;

                if (m < 1 || n < 1)
                    continue;

                var (i, j) = backward ? (m, loop.Length - 1) : (1, m + 2);

                if (mesh.FindEdge(loop[i], loop[j]) >= 0)
                    continue;

                faces[first] = loop.Span(i, j);
                faces[second] = loop.Span(j, i);
                touched.Add(first);
                touched.Add(second);
                turned.Add(loop[i]);
                turned.Add(loop[j]);
            }

            return turned.Count == 0 ? null : (mesh.With(mesh.Points, faces, mesh.Groups, mesh.Smooth), [.. turned]);
        }

        public static (MeshTopology Mesh, int[] Added)? FlipNormals(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var chosen = new HashSet<int>(mesh.Existing(faces));

            if (chosen.Count == 0)
                return null;

            var flipped = mesh.Faces.Select((face, index) => chosen.Contains(index) ? [.. face.Reverse()] : face);

            return (mesh.With(mesh.Points, flipped, mesh.Groups, mesh.Smooth), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        public static (MeshTopology Mesh, int[] Added)? RecalculateNormals(this MeshTopology mesh, IReadOnlyCollection<int> faces, bool inside)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length == 0)
                return null;

            var result = mesh.Faces.Select(face => face).ToArray();

            foreach (var island in mesh.Islands(chosen, edge => mesh.EdgeFaces[edge].Length == 2))
            {
                var turned = Consistent(mesh, island);
                var middle = mesh.Middle([.. island.SelectMany(face => mesh.Faces[face]).Distinct()]);
                var volume = 0f;

                foreach (var face in island)
                {
                    var corners = turned[face];
                    var center = mesh.Middle(corners);

                    volume += Vector3.Dot(Area(mesh, corners), center - middle);
                }

                var flips = (volume > 0f) != inside;

                foreach (var face in island)
                    result[face] = flips ? [.. turned[face].Reverse()] : turned[face];
            }

            return (mesh.With(mesh.Points, result, mesh.Groups, mesh.Smooth), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        private static Dictionary<int, int[]> Consistent(MeshTopology mesh, List<int> island)
        {
            var inside = new HashSet<int>(island);
            var turned = new Dictionary<int, int[]> { [island[0]] = mesh.Faces[island[0]] };
            var queue = new Queue<int>();

            queue.Enqueue(island[0]);

            while (queue.Count > 0)
            {
                var face = queue.Dequeue();
                var corners = turned[face];

                for (var i = 0; i < corners.Length; i++)
                {
                    var (a, b) = (corners[i], corners[(i + 1) % corners.Length]);
                    var edge = mesh.FindEdge(a, b);

                    if (mesh.EdgeFaces[edge] is not [var first, var second])
                        continue;

                    var other = first == face ? second : first;

                    if (!inside.Contains(other) || turned.ContainsKey(other))
                        continue;

                    var next = mesh.Faces[other];

                    turned[other] = next.Runs(a, b) ? [.. next.Reverse()] : next;
                    queue.Enqueue(other);
                }
            }

            foreach (var face in island)
                turned.TryAdd(face, mesh.Faces[face]);

            return turned;
        }

        private static Vector3 Area(MeshTopology mesh, int[] corners)
        {
            var sum = Vector3.Zero;
            var origin = mesh.Points[corners[0]];

            for (var i = 1; i + 1 < corners.Length; i++)
                sum += Vector3.Cross(mesh.Points[corners[i]] - origin, mesh.Points[corners[i + 1]] - origin);

            return sum * 0.5f;
        }

        private static int[] From(int[] face, int start)
        {
            var at = Array.IndexOf(face, start);

            return [.. Enumerable.Range(0, face.Length).Select(i => face[(at + i) % face.Length])];
        }

        private static int[]? Joined(MeshTopology mesh, int edge, int first, int second)
        {
            var (a, b) = (mesh.Edges[edge].A, mesh.Edges[edge].B);

            if (!mesh.Faces[first].Runs(a, b))
                (a, b) = (b, a);

            if (!mesh.Faces[second].Runs(b, a))
                return null;

            var r1 = mesh.Faces[first].First(one => one != a && one != b);
            var r2 = mesh.Faces[second].First(one => one != a && one != b);

            return r1 == r2 ? null : [a, r2, b, r1];
        }

        private static float Shape(MeshTopology mesh, int[] quad)
        {
            var worst = 0f;

            for (var i = 0; i < 4; i++)
            {
                var here = mesh.Points[quad[i]];
                var before = mesh.Points[quad[(i + 3) % 4]] - here;
                var after = mesh.Points[quad[(i + 1) % 4]] - here;

                worst = MathF.Max(worst, MathF.Abs(Angle(before, after) - MathF.PI / 2f));
            }

            return worst;
        }

        private static float Angle(Vector3 first, Vector3 second)
        {
            if (first.LengthSquared() < 1e-20f || second.LengthSquared() < 1e-20f)
                return MathF.PI;

            return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(first), Vector3.Normalize(second)), -1f, 1f));
        }

        private static List<int[]> Ears(MeshTopology mesh, int face)
        {
            var normal = mesh.NormalOf(face);
            var ring = mesh.Faces[face].ToList();
            var made = new List<int[]>();

            if (normal.LengthSquared() < 1e-12f)
                normal = Vector3.UnitY;

            var guess = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(guess, normal));
            var up = Vector3.Cross(normal, right);

            Vector2 Flat(int one) => new(Vector3.Dot(mesh.Points[one], right), Vector3.Dot(mesh.Points[one], up));

            var turn = 0f;

            for (var i = 0; i < ring.Count; i++)
            {
                var (p, q) = (Flat(ring[i]), Flat(ring[(i + 1) % ring.Count]));

                turn += p.X * q.Y - q.X * p.Y;
            }

            var sign = turn >= 0f ? 1f : -1f;

            while (ring.Count > 3)
            {
                var best = -1;
                var shortest = float.MaxValue;

                for (var i = 0; i < ring.Count; i++)
                {
                    var (before, here, after) = (ring[(i + ring.Count - 1) % ring.Count], ring[i], ring[(i + 1) % ring.Count]);
                    var (a, b, c) = (Flat(before), Flat(here), Flat(after));

                    if (Cross(a, b, c) * sign <= 1e-12f)
                        continue;

                    var blocked = false;

                    foreach (var other in ring)
                    {
                        if (other == before || other == here || other == after)
                            continue;

                        if (Inside(Flat(other), a, b, c, sign))
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (blocked)
                        continue;

                    var length = Vector3.DistanceSquared(mesh.Points[before], mesh.Points[after]);

                    if (length < shortest)
                    {
                        shortest = length;
                        best = i;
                    }
                }

                if (best < 0)
                    best = 0;

                made.Add([ring[(best + ring.Count - 1) % ring.Count], ring[best], ring[(best + 1) % ring.Count]]);
                ring.RemoveAt(best);
            }

            made.Add([.. ring]);

            return made;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static bool Inside(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float sign)
            => Cross(a, b, point) * sign >= 0f && Cross(b, c, point) * sign >= 0f && Cross(c, a, point) * sign >= 0f;
    }
}
