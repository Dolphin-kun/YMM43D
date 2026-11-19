using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshEdgeBevel
    {
        private const float Overlap = 0.49f;

        public static MeshTopology Beveled(this MeshTopology mesh, float amount, int segments, float angleLimit)
        {
            if (amount <= 0f || mesh.Faces.Length == 0)
                return mesh;

            var limit = MathF.Cos(angleLimit);
            var beveled = new HashSet<int>();

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                if (mesh.EdgeFaces[e] is [var first, var second] && (angleLimit <= 0f || Vector3.Dot(mesh.NormalOf(first), mesh.NormalOf(second)) < limit))
                    beveled.Add(e);
            }

            if (beveled.Count == 0)
                return mesh;

            return new Builder(mesh, beveled, amount, Math.Max(1, segments)).Build();
        }

        private sealed class Builder(MeshTopology mesh, HashSet<int> beveled, float amount, int segments)
        {
            private readonly List<Vector3> points = [.. mesh.Points];
            private readonly Dictionary<(int Face, int Vertex), int[]> corners = [];
            private readonly Dictionary<(int Vertex, int Edge), int> slides = [];
            private readonly Dictionary<(int Vertex, int Edge), int[]> profiles = [];
            private readonly List<(int[] Corners, int Group, bool Smooth)> made = [];

            public MeshTopology Build()
            {
                for (var v = 0; v < mesh.Points.Length; v++)
                    PlaceCorners(v);

                foreach (var edge in beveled)
                {
                    foreach (var vertex in new[] { mesh.Edges[edge].A, mesh.Edges[edge].B })
                        profiles[(vertex, edge)] = Profile(vertex, edge);
                }

                for (var f = 0; f < mesh.Faces.Length; f++)
                    made.Add((Rebuilt(f), mesh.Groups[f], mesh.Smooth[f]));

                foreach (var edge in beveled)
                    AddStrip(edge);

                for (var v = 0; v < mesh.Points.Length; v++)
                    AddPatch(v);

                var faces = made.Where(face => face.Corners.Length >= 3).ToArray();
                var (built, _) = MeshPacking.Pack(points, [.. faces.Select(face => face.Corners)], [.. faces.Select(face => face.Group)], [.. faces.Select(face => face.Smooth)], mesh.LooseEdges, mesh.IsIsolated, mesh.GroupCount);

                return built.Carry(mesh);
            }

            private int Add(Vector3 place)
            {
                points.Add(place);

                return points.Count - 1;
            }

            private (int In, int Out) EdgesAt(int face, int vertex)
            {
                var ring = mesh.Faces[face];
                var at = Array.IndexOf(ring, vertex);

                return (mesh.FaceEdges[face][(at + ring.Length - 1) % ring.Length], mesh.FaceEdges[face][at]);
            }

            private int[] FacesAt(int vertex) => [.. mesh.VertexEdges[vertex].SelectMany(edge => mesh.EdgeFaces[edge]).Distinct()];

            private int BeveledCount(int vertex) => mesh.VertexEdges[vertex].Count(beveled.Contains);

            private bool IsTerminal(int vertex) => BeveledCount(vertex) == 1 && mesh.VertexEdges[vertex].Length == 3 && FacesAt(vertex).Length == 3;

            private Vector3 Direction(int vertex, int edge) => Vector3.Normalize(mesh.Points[mesh.Edges[edge].Other(vertex)] - mesh.Points[vertex]);

            private float Length(int edge) => Vector3.Distance(mesh.Points[mesh.Edges[edge].A], mesh.Points[mesh.Edges[edge].B]);

            private void PlaceCorners(int vertex)
            {
                if (BeveledCount(vertex) == 0)
                    return;

                var here = mesh.Points[vertex];

                foreach (var face in FacesAt(vertex))
                {
                    var (edgeIn, edgeOut) = EdgesAt(face, vertex);
                    var (inBeveled, outBeveled) = (beveled.Contains(edgeIn), beveled.Contains(edgeOut));

                    if (inBeveled && outBeveled)
                    {
                        var (u, w) = (Direction(vertex, edgeIn), Direction(vertex, edgeOut));
                        var half = MathF.Acos(Math.Clamp(Vector3.Dot(u, w), -1f, 1f)) / 2f;
                        var reach = MathF.Min(amount / MathF.Max(MathF.Sin(half), 1e-3f), Overlap * MathF.Min(Length(edgeIn), Length(edgeOut)) / MathF.Max(MathF.Cos(half), 1e-3f));
                        var bisector = u + w;

                        corners[(face, vertex)] = [Add(here + (bisector.LengthSquared() > 1e-12f ? Vector3.Normalize(bisector) : u) * reach)];
                    }
                    else if (inBeveled || outBeveled)
                    {
                        corners[(face, vertex)] = [Slide(vertex, inBeveled ? edgeOut : edgeIn)];
                    }
                    else if (!IsTerminal(vertex))
                    {
                        corners[(face, vertex)] = [Slide(vertex, edgeIn), Slide(vertex, edgeOut)];
                    }
                }
            }

            private int Slide(int vertex, int edge)
            {
                if (slides.TryGetValue((vertex, edge), out var known))
                    return known;

                var direction = Direction(vertex, edge);
                var distances = new List<float>();

                foreach (var face in mesh.EdgeFaces[edge])
                {
                    var (edgeIn, edgeOut) = EdgesAt(face, vertex);
                    var other = edgeIn == edge ? edgeOut : edgeIn;

                    if (!beveled.Contains(other))
                        continue;

                    var sine = Vector3.Cross(direction, Direction(vertex, other)).Length();

                    distances.Add(amount / MathF.Max(sine, 1e-3f));
                }

                var distance = MathF.Min(distances.Count > 0 ? distances.Average() : amount, Overlap * Length(edge));
                var made = Add(mesh.Points[vertex] + direction * distance);

                slides[(vertex, edge)] = made;

                return made;
            }

            private (int From, int To) Sides(int edge)
            {
                var (a, b) = (mesh.Edges[edge].A, mesh.Edges[edge].B);
                var (first, second) = (mesh.EdgeFaces[edge][0], mesh.EdgeFaces[edge][1]);

                return mesh.Faces[first].Runs(a, b) ? (first, second) : (second, first);
            }

            private int[] Profile(int vertex, int edge)
            {
                var (from, to) = Sides(edge);
                var start = corners[(from, vertex)][0];
                var end = corners[(to, vertex)][0];
                var chain = new int[segments + 1];

                chain[0] = start;
                chain[segments] = end;

                if (segments == 1)
                    return chain;

                var here = mesh.Points[vertex];
                var (p, q) = (points[start] - here, points[end] - here);
                var leg = (p.Length() + q.Length()) / 2f;
                var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(p), Vector3.Normalize(q)), -1f, 1f));
                var bisector = Vector3.Normalize(p + q);
                var center = here + bisector * (leg / MathF.Max(MathF.Cos(angle / 2f), 1e-3f));
                var (fromCenter, toCenter) = (points[start] - center, points[end] - center);

                for (var i = 1; i < segments; i++)
                    chain[i] = Add(center + Slerp(fromCenter, toCenter, (float)i / segments));

                return chain;
            }

            private int[] Rebuilt(int face)
            {
                var ring = mesh.Faces[face];
                var result = new List<int>();

                foreach (var vertex in ring)
                {
                    if (corners.TryGetValue((face, vertex), out var replaced))
                        result.AddRange(replaced);
                    else if (IsTerminal(vertex))
                        result.AddRange(TerminalChain(face, vertex));
                    else
                        result.Add(vertex);
                }

                return Collapsed(result);
            }

            private int[] TerminalChain(int face, int vertex)
            {
                var edge = mesh.VertexEdges[vertex].First(beveled.Contains);
                var chain = profiles[(vertex, edge)];
                var (edgeIn, _) = EdgesAt(face, vertex);

                return chain[0] == slides.GetValueOrDefault((vertex, edgeIn), -1) ? chain : [.. chain.Reverse()];
            }

            private void AddStrip(int edge)
            {
                var (from, _) = Sides(edge);
                var (a, b) = (mesh.Edges[edge].A, mesh.Edges[edge].B);
                var (pa, pb) = (profiles[(a, edge)], profiles[(b, edge)]);
                var smooth = segments > 1;

                for (var i = 0; i < segments; i++)
                    made.Add((Collapsed([pb[i], pa[i], pa[i + 1], pb[i + 1]]), mesh.Groups[from], smooth));
            }

            private void AddPatch(int vertex)
            {
                if (BeveledCount(vertex) == 0 || IsTerminal(vertex) || Fan(vertex) is not { } fan)
                    return;

                var loop = new List<int>();

                for (var i = 0; i < fan.Length; i++)
                {
                    var face = fan[i];

                    loop.AddRange(corners[(face, vertex)]);

                    var (_, edgeOut) = EdgesAt(face, vertex);

                    if (!beveled.Contains(edgeOut))
                        continue;

                    var chain = profiles[(vertex, edgeOut)];
                    var ordered = Sides(edgeOut).From == face ? chain : [.. chain.Reverse()];

                    loop.AddRange(ordered[1..^1]);
                }

                var boundary = Collapsed(loop);

                if (boundary.Length < 3)
                    return;

                boundary = [.. boundary.Reverse()];

                var group = mesh.Groups[fan[0]];

                if (segments == 1)
                {
                    made.Add((boundary, group, false));
                    return;
                }

                var sphere = SphereCenter(vertex, fan);
                var rings = new List<int[]> { boundary };
                var middle = boundary.Aggregate(Vector3.Zero, (sum, one) => sum + points[one]) / boundary.Length;

                for (var r = 1; r <= segments / 2; r++)
                {
                    var t = (float)r / (segments / 2 + 1);

                    rings.Add([.. boundary.Select(one => Add(OnSphere(Vector3.Lerp(points[one], middle, t), sphere)))]);
                }

                for (var r = 0; r + 1 < rings.Count; r++)
                {
                    var (outer, inner) = (rings[r], rings[r + 1]);

                    for (var j = 0; j < outer.Length; j++)
                    {
                        var next = (j + 1) % outer.Length;

                        made.Add(([outer[j], outer[next], inner[next], inner[j]], group, true));
                    }
                }

                made.Add((rings[^1], group, true));
            }

            private int[]? Fan(int vertex)
            {
                var faces = FacesAt(vertex);

                if (faces.Length == 0 || mesh.VertexEdges[vertex].Any(edge => mesh.EdgeFaces[edge].Length != 2))
                    return null;

                var order = new List<int> { faces[0] };

                while (order.Count < faces.Length)
                {
                    var (_, edgeOut) = EdgesAt(order[^1], vertex);
                    var next = mesh.EdgeFaces[edgeOut].First(face => face != order[^1]);

                    if (next == order[0])
                        break;

                    order.Add(next);
                }

                return order.Count == faces.Length ? [.. order] : null;
            }

            private (Vector3 Center, float Radius) SphereCenter(int vertex, int[] fan)
            {
                var here = mesh.Points[vertex];
                var matrix = new Matrix4x4();
                var sum = Vector3.Zero;

                foreach (var face in fan)
                {
                    var n = mesh.NormalOf(face);

                    matrix.M11 += n.X * n.X;
                    matrix.M12 += n.X * n.Y;
                    matrix.M13 += n.X * n.Z;
                    matrix.M22 += n.Y * n.Y;
                    matrix.M23 += n.Y * n.Z;
                    matrix.M33 += n.Z * n.Z;
                    sum += n;
                }

                matrix.M21 = matrix.M12;
                matrix.M31 = matrix.M13;
                matrix.M32 = matrix.M23;
                matrix.M44 = 1f;

                var offset = Matrix4x4.Invert(matrix, out var inverse) && MathF.Abs(matrix.GetDeterminant()) > 1e-6f
                    ? Vector3.Transform(-sum * amount, inverse)
                    : -Vector3.Normalize(sum) * amount;

                return (here + offset, amount);
            }

            private static Vector3 OnSphere(Vector3 place, (Vector3 Center, float Radius) sphere)
            {
                var away = place - sphere.Center;

                return away.LengthSquared() < 1e-20f ? place : sphere.Center + Vector3.Normalize(away) * sphere.Radius;
            }

            private static Vector3 Slerp(Vector3 from, Vector3 to, float t)
            {
                var (lengthFrom, lengthTo) = (from.Length(), to.Length());
                var (a, b) = (from / MathF.Max(lengthFrom, 1e-12f), to / MathF.Max(lengthTo, 1e-12f));
                var angle = MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f));
                var length = lengthFrom + (lengthTo - lengthFrom) * t;

                if (angle < 1e-4f)
                    return Vector3.Lerp(from, to, t);

                var direction = (MathF.Sin((1f - t) * angle) * a + MathF.Sin(t * angle) * b) / MathF.Sin(angle);

                return direction * length;
            }

            private static int[] Collapsed(List<int> ring)
            {
                var result = new List<int>();

                foreach (var one in ring)
                {
                    if (result.Count == 0 || result[^1] != one)
                        result.Add(one);
                }

                while (result.Count > 1 && result[0] == result[^1])
                    result.RemoveAt(result.Count - 1);

                return [.. result];
            }
        }
    }
}
