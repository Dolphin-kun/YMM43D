using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshBridge
    {
        public static (MeshTopology Mesh, int[] Added)? Bridge(this MeshTopology mesh, IReadOnlyCollection<int> faces, IReadOnlyCollection<int> edges, int cuts)
        {
            var chosen = mesh.Existing(faces);
            var source = mesh;
            List<(int[] Vertices, bool Closed)> rails;

            if (chosen.Length > 0)
            {
                if (mesh.Islands(chosen) is not [var first, var second]
                    || mesh.BoundaryLoops(first) is not [var one]
                    || mesh.BoundaryLoops(second) is not [var other]
                    || mesh.DeleteOnlyFaces(chosen) is not { } opened)
                {
                    return null;
                }

                source = opened;
                rails = [(one, true), (other, true)];
            }
            else
            {
                if (mesh.Chains(mesh.ExistingEdges(edges)) is not [var first, var second]
                    || mesh.Ordered(first) is not { } one
                    || mesh.Ordered(second) is not { } other
                    || one.Closed != other.Closed)
                {
                    return null;
                }

                rails = [one, other];
            }

            var closed = rails[0].Closed;
            var a = source.Oriented(rails[0].Vertices);
            var b = Aligned(source, a, rails[1].Vertices, closed);
            var points = new List<Vector3>(source.Points);
            var rings = new List<int[]> { a };
            var spanA = Spans(source.Points, a, closed);
            var spanB = Spans(source.Points, b, closed);

            for (var k = 1; k <= Math.Max(0, cuts); k++)
            {
                var ring = new int[a.Length];
                var t = k / (cuts + 1f);

                for (var i = 0; i < a.Length; i++)
                {
                    ring[i] = points.Count;
                    points.Add(Vector3.Lerp(source.Points[a[i]], Sample(source.Points, b, spanB, spanA[i], closed), t));
                }

                rings.Add(ring);
            }

            rings.Add(b);

            var made = new List<int[]>();

            for (var k = 0; k + 1 < rings.Count; k++)
            {
                var last = k + 2 == rings.Count;

                made.AddRange(Zipped(rings[k], spanA, rings[k + 1], last ? spanB : spanA, closed));
            }

            if (Flips(source, a, b, made))
                made = [.. made.Select(face => face.Reverse().ToArray())];

            var faceList = new List<int[]>(source.Faces);
            var groups = new List<int>(source.Groups);
            var smooth = new List<bool>(source.Smooth);
            var group = chosen.Length > 0 ? mesh.Groups[chosen[0]] : 0;

            foreach (var face in made)
            {
                faceList.Add(face);
                groups.Add(group);
                smooth.Add(false);
            }

            var added = rings.SelectMany(ring => ring).Distinct();

            return source.Rebuilt([.. points], faceList, groups, smooth, source.LooseEdges, source.IsIsolated, added);
        }

        private static bool Flips(MeshTopology mesh, int[] a, int[] b, List<int[]> made)
        {
            for (var i = 0; i < a.Length; i++)
            {
                if (mesh.FindEdge(a[i], a[(i + 1) % a.Length]) is var edge && edge >= 0 && mesh.EdgeFaces[edge].Length == 1)
                    return false;
            }

            var onB = new HashSet<int>(b);

            foreach (var face in made)
            {
                for (var i = 0; i < face.Length; i++)
                {
                    var (p, q) = (face[i], face[(i + 1) % face.Length]);

                    if (!onB.Contains(p) || !onB.Contains(q))
                        continue;

                    if (mesh.FindEdge(p, q) is var edge && edge >= 0 && mesh.EdgeFaces[edge].Length == 1)
                        return mesh.Faces[mesh.EdgeFaces[edge][0]].Runs(p, q);
                }
            }

            return false;
        }

        private static float[] Spans(Vector3[] points, int[] rail, bool closed)
        {
            var spans = new float[rail.Length];
            var total = 0f;

            for (var i = 1; i < rail.Length; i++)
            {
                total += Vector3.Distance(points[rail[i - 1]], points[rail[i]]);
                spans[i] = total;
            }

            if (closed)
                total += Vector3.Distance(points[rail[^1]], points[rail[0]]);

            for (var i = 0; i < spans.Length; i++)
                spans[i] = total > 1e-9f ? spans[i] / total : (float)i / (closed ? rail.Length : Math.Max(1, rail.Length - 1));

            return spans;
        }

        private static Vector3 Sample(Vector3[] points, int[] rail, float[] spans, float at, bool closed)
        {
            var count = closed ? rail.Length : rail.Length - 1;

            for (var i = 0; i < count; i++)
            {
                var start = spans[i];
                var end = i + 1 < rail.Length ? spans[i + 1] : 1f;

                if (at > end && i + 1 < count)
                    continue;

                var t = end - start > 1e-9f ? Math.Clamp((at - start) / (end - start), 0f, 1f) : 0f;

                return Vector3.Lerp(points[rail[i]], points[rail[(i + 1) % rail.Length]], t);
            }

            return points[rail[^1]];
        }

        private static int[] Aligned(MeshTopology mesh, int[] a, int[] b, bool closed)
        {
            var spanA = Spans(mesh.Points, a, closed);
            var best = b;
            var lowest = float.MaxValue;

            foreach (var direction in new[] { b, [.. b.Reverse()] })
            {
                var shifts = closed ? direction.Length : 1;

                for (var shift = 0; shift < shifts; shift++)
                {
                    int[] turned = [.. Enumerable.Range(0, direction.Length).Select(i => direction[(i + shift) % direction.Length])];
                    var spanB = Spans(mesh.Points, turned, closed);
                    var cost = 0f;

                    for (var i = 0; i < a.Length; i++)
                        cost += Vector3.DistanceSquared(mesh.Points[a[i]], Sample(mesh.Points, turned, spanB, spanA[i], closed));

                    if (cost < lowest)
                    {
                        lowest = cost;
                        best = turned;
                    }
                }
            }

            return best;
        }

        private static List<int[]> Zipped(int[] lower, float[] lowerSpans, int[] upper, float[] upperSpans, bool closed)
        {
            int[] l = closed ? [.. lower, lower[0]] : lower;
            int[] u = closed ? [.. upper, upper[0]] : upper;
            float[] ls = closed ? [.. lowerSpans, 1f] : lowerSpans;
            float[] us = closed ? [.. upperSpans, 1f] : upperSpans;
            var even = lower.Length == upper.Length;
            var faces = new List<int[]>();
            var (i, j) = (0, 0);

            while (i < l.Length - 1 || j < u.Length - 1)
            {
                if (i == l.Length - 1)
                {
                    faces.Add([l[i], u[j + 1], u[j]]);
                    j++;
                    continue;
                }

                if (j == u.Length - 1)
                {
                    faces.Add([l[i], l[i + 1], u[j]]);
                    i++;
                    continue;
                }

                var (nextLower, nextUpper) = (ls[i + 1], us[j + 1]);
                var tolerance = 0.25f * MathF.Min(ls[i + 1] - ls[i], us[j + 1] - us[j]);

                if (even || MathF.Abs(nextLower - nextUpper) <= tolerance)
                {
                    faces.Add([l[i], l[i + 1], u[j + 1], u[j]]);
                    i++;
                    j++;
                }
                else if (nextLower < nextUpper)
                {
                    faces.Add([l[i], l[i + 1], u[j]]);
                    i++;
                }
                else
                {
                    faces.Add([l[i], u[j + 1], u[j]]);
                    j++;
                }
            }

            return faces;
        }
    }
}
