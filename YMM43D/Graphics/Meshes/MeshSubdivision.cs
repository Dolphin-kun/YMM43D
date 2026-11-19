using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshSubdivision
    {
        private const float SharpnessPerCrease = 10f;

        public static MeshTopology CatmullClark(this MeshTopology mesh, int levels)
        {
            var sharpness = mesh.Marks.Where(pair => pair.Value.Crease > 0f).ToDictionary(pair => pair.Key, pair => SharpnessPerCrease * pair.Value.Crease * pair.Value.Crease);
            var sharp = mesh.Marks.Where(pair => pair.Value.Sharp).Select(pair => pair.Key).ToHashSet();
            var current = mesh;

            for (var level = 0; level < levels && current.Faces.Length > 0; level++)
                (current, sharpness, sharp) = Step(current, sharpness, sharp);

            return current.WithMarks(sharp.ToDictionary(edge => edge, _ => new EdgeMark(true, 0f)));
        }

        private static (MeshTopology Mesh, Dictionary<MeshEdge, float> Sharpness, HashSet<MeshEdge> Sharp) Step(MeshTopology mesh, Dictionary<MeshEdge, float> sharpness, HashSet<MeshEdge> sharp)
        {
            var pointCount = mesh.Points.Length;
            var edgeBase = pointCount;
            var faceBase = edgeBase + mesh.Edges.Length;
            var points = new Vector3[faceBase + mesh.Faces.Length];
            var edgeSharpness = new float[mesh.Edges.Length];

            for (var e = 0; e < mesh.Edges.Length; e++)
                edgeSharpness[e] = mesh.EdgeFaces[e].Length == 2 ? sharpness.GetValueOrDefault(mesh.Edges[e]) : float.PositiveInfinity;

            for (var f = 0; f < mesh.Faces.Length; f++)
                points[faceBase + f] = mesh.Middle(mesh.Faces[f]);

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                var (a, b) = (mesh.Points[mesh.Edges[e].A], mesh.Points[mesh.Edges[e].B]);
                var middle = (a + b) / 2f;

                if (mesh.EdgeFaces[e] is not [var first, var second])
                {
                    points[edgeBase + e] = middle;
                    continue;
                }

                var smooth = (a + b + points[faceBase + first] + points[faceBase + second]) / 4f;

                points[edgeBase + e] = Vector3.Lerp(smooth, middle, Math.Clamp(edgeSharpness[e], 0f, 1f));
            }

            for (var v = 0; v < pointCount; v++)
                points[v] = VertexPoint(mesh, v, points, faceBase, edgeSharpness);

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smoothFlags = new List<bool>();
            var uvs = mesh.Uvs is null ? null : new List<Vector2[]>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f];
                var edges = mesh.FaceEdges[f];
                var n = corners.Length;

                for (var i = 0; i < n; i++)
                {
                    var previous = edges[(i + n - 1) % n];

                    faces.Add([corners[i], edgeBase + edges[i], faceBase + f, edgeBase + previous]);
                    groups.Add(mesh.Groups[f]);
                    smoothFlags.Add(mesh.Smooth[f]);

                    if (mesh.Uvs is { } source)
                    {
                        var face = source[f];
                        var middle = face.Aggregate(Vector2.Zero, (sum, uv) => sum + uv) / n;

                        uvs!.Add([face[i], (face[i] + face[(i + 1) % n]) / 2f, middle, (face[(i + n - 1) % n] + face[i]) / 2f]);
                    }
                }
            }

            var loose = new List<MeshEdge>();
            var nextSharpness = new Dictionary<MeshEdge, float>();
            var nextSharp = new HashSet<MeshEdge>();

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                var edge = mesh.Edges[e];
                MeshEdge[] halves = [MeshTopology.Key(edge.A, edgeBase + e), MeshTopology.Key(edgeBase + e, edge.B)];

                if (mesh.IsLoose(e))
                    loose.AddRange(halves);

                var left = sharpness.GetValueOrDefault(edge) - 1f;

                foreach (var half in halves)
                {
                    if (left > 0f)
                        nextSharpness[half] = left;

                    if (sharp.Contains(edge))
                        nextSharp.Add(half);
                }
            }

            var made = new MeshTopology(points, [.. faces], [.. groups], [.. smoothFlags], mesh.GroupCount, loose, null, uvs is null ? null : [.. uvs]);

            return (made, nextSharpness, nextSharp);
        }

        private static Vector3 VertexPoint(MeshTopology mesh, int vertex, Vector3[] points, int faceBase, float[] edgeSharpness)
        {
            var here = mesh.Points[vertex];
            var edges = mesh.VertexEdges[vertex];

            if (edges.Length == 0)
                return here;

            var creased = edges.Where(edge => edgeSharpness[edge] > 0f).ToArray();
            var faces = edges.SelectMany(edge => mesh.EdgeFaces[edge]).Distinct().ToArray();

            if (faces.Length == 0 && creased.Length != 2)
                return here;

            var sharpRule = creased.Length switch
            {
                < 2 => (Vector3?)null,
                2 => (6f * here + mesh.Points[mesh.Edges[creased[0]].Other(vertex)] + mesh.Points[mesh.Edges[creased[1]].Other(vertex)]) / 8f,
                _ => here,
            };

            if (faces.Length == 0)
                return sharpRule ?? here;

            var n = edges.Length;
            var q = faces.Aggregate(Vector3.Zero, (sum, face) => sum + points[faceBase + face]) / faces.Length;
            var r = edges.Aggregate(Vector3.Zero, (sum, edge) => sum + (mesh.Points[mesh.Edges[edge].A] + mesh.Points[mesh.Edges[edge].B]) / 2f) / n;
            var smooth = (q + 2f * r + (n - 3f) * here) / n;

            if (sharpRule is not { } rule)
                return smooth;

            var weight = Math.Clamp(creased.Average(edge => MathF.Min(edgeSharpness[edge], 1f)), 0f, 1f);

            return Vector3.Lerp(smooth, rule, weight);
        }
    }
}
