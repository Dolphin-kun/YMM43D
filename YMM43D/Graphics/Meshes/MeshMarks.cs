using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshMarks
    {
        private const float Weld = 1e-4f;

        public static (MeshTopology Mesh, int[] Added)? Marked(this MeshTopology mesh, IReadOnlyCollection<int> edges, Func<EdgeMark, EdgeMark> change)
        {
            var chosen = mesh.ExistingEdges(edges);

            if (chosen.Length == 0)
                return null;

            var marks = new Dictionary<MeshEdge, EdgeMark>(mesh.Marks);

            foreach (var edge in chosen)
                marks[mesh.Edges[edge]] = change(mesh.MarkOf(edge));

            return (mesh.WithMarks(marks), [.. chosen.SelectMany(edge => new[] { mesh.Edges[edge].A, mesh.Edges[edge].B }).Distinct()]);
        }

        public static (MeshTopology Mesh, int[] Added)? Shaded(this MeshTopology mesh, IReadOnlyCollection<int> faces, bool smooth)
        {
            var chosen = new HashSet<int>(mesh.Existing(faces));

            if (chosen.Count == 0)
                return null;

            var flags = mesh.Smooth.Select((flag, face) => chosen.Contains(face) ? smooth : flag);

            return (mesh.With(mesh.Points, mesh.Faces, mesh.Groups, flags).WithUvs(mesh.Uvs).WithKeys(mesh.Keys).WithWeights(mesh.Weights), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        public static (MeshTopology Mesh, int[] Added)? Assigned(this MeshTopology mesh, IReadOnlyCollection<int> faces, int group)
        {
            var chosen = new HashSet<int>(mesh.Existing(faces));

            if (chosen.Count == 0 || group < 0 || chosen.All(face => mesh.Groups[face] == group))
                return null;

            var groups = mesh.Groups.Select((known, face) => chosen.Contains(face) ? group : known);

            return (mesh.With(mesh.Points, mesh.Faces, groups, mesh.Smooth).WithUvs(mesh.Uvs).WithKeys(mesh.Keys).WithWeights(mesh.Weights), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        public static int[] FacesOf(this MeshTopology mesh, int group)
            => [.. Enumerable.Range(0, mesh.Faces.Length).Where(face => mesh.Groups[face] == group)];

        public static float AverageCrease(this MeshTopology mesh, IReadOnlyCollection<int> edges)
        {
            var chosen = mesh.ExistingEdges(edges);

            return chosen.Length == 0 ? 0f : chosen.Average(edge => mesh.MarkOf(edge).Crease);
        }

        public static MeshTopology Carry(this MeshTopology to, MeshTopology from)
        {
            if ((from.Marks.Count == 0 && from.Uvs is null && from.Keys is null && from.Weights is null) || (ReferenceEquals(to.Points, from.Points) && ReferenceEquals(to.Faces, from.Faces)))
                return to;

            var uvs = to.Uvs ?? MeshUv.Carried(to, from);
            var keys = to.Keys ?? MeshUv.CarriedKeys(to, from);
            var weights = to.Weights ?? MeshUv.CarriedWeights(to, from);

            if (from.Marks.Count == 0)
                return to.WithUvs(uvs).WithKeys(keys).WithWeights(weights);

            var places = new Dictionary<(long, long, long), int>();

            for (var i = 0; i < to.Points.Length; i++)
                places.TryAdd(KeyOf(to.Points[i]), i);

            var marks = new Dictionary<MeshEdge, EdgeMark>(to.Marks);
            var lost = new List<(Vector3 A, Vector3 B, EdgeMark Mark)>();

            foreach (var (edge, mark) in from.Marks)
            {
                var (a, b) = (from.Points[edge.A], from.Points[edge.B]);

                if (places.TryGetValue(KeyOf(a), out var na)
                    && places.TryGetValue(KeyOf(b), out var nb)
                    && to.FindEdge(na, nb) >= 0)
                {
                    marks.TryAdd(MeshTopology.Key(na, nb), mark);
                    continue;
                }

                lost.Add((a, b, mark));
            }

            if (lost.Count > 0)
            {
                foreach (var edge in to.Edges)
                {
                    if (marks.ContainsKey(edge))
                        continue;

                    var (p, q) = (to.Points[edge.A], to.Points[edge.B]);

                    foreach (var (a, b, mark) in lost)
                    {
                        if (OnSegment(p, a, b) && OnSegment(q, a, b))
                        {
                            marks[edge] = mark;
                            break;
                        }
                    }
                }
            }

            return to.WithMarks(marks).WithUvs(uvs).WithKeys(keys).WithWeights(weights);
        }

        private static bool OnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            var span = b - a;
            var length = span.LengthSquared();

            if (length < 1e-20f)
                return false;

            var t = Vector3.Dot(point - a, span) / length;

            if (t is < -Weld or > 1f + Weld)
                return false;

            return Vector3.DistanceSquared(point, a + span * t) <= Weld * Weld * MathF.Max(1f, length);
        }

        private static (long, long, long) KeyOf(in Vector3 point)
            => ((long)MathF.Round(point.X / Weld), (long)MathF.Round(point.Y / Weld), (long)MathF.Round(point.Z / Weld));
    }
}
