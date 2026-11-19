using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshDissolve
    {
        private const float StraightCos = 0.996f;

        public static (MeshTopology Mesh, int[] Added)? DissolveFaces(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length < 2)
                return null;

            return Merged(mesh, mesh.Islands(chosen), [], []);
        }

        public static (MeshTopology Mesh, int[] Added)? DissolveEdges(this MeshTopology mesh, IReadOnlyCollection<int> edges, bool dropsVertices = true)
        {
            var chosen = new HashSet<int>(mesh.ExistingEdges(edges));

            if (chosen.Count == 0)
                return null;

            var touching = chosen.SelectMany(edge => mesh.EdgeFaces[edge]).Distinct().ToArray();
            var islands = mesh.Islands(touching, edge => chosen.Contains(edge) && mesh.EdgeFaces[edge].Length == 2);
            var ends = dropsVertices ? chosen.SelectMany(edge => new[] { mesh.Edges[edge].A, mesh.Edges[edge].B }).ToHashSet() : [];

            return Merged(mesh, islands, [.. chosen.Where(mesh.IsLoose)], ends);
        }

        public static (MeshTopology Mesh, int[] Added)? DissolveVertices(this MeshTopology mesh, IReadOnlyCollection<int> vertices)
        {
            var chosen = new HashSet<int>(mesh.ExistingPoints(vertices));

            if (chosen.Count == 0)
                return null;

            var around = chosen.SelectMany(vertex => mesh.VertexEdges[vertex]).SelectMany(edge => mesh.EdgeFaces[edge]).Distinct().ToArray();
            var islands = mesh.Islands(around, edge => Spreads(mesh, chosen, mesh.Edges[edge].A) || Spreads(mesh, chosen, mesh.Edges[edge].B));

            return Merged(mesh, islands, [], chosen, true);
        }

        private static (MeshTopology Mesh, int[] Added)? Merged(MeshTopology mesh, List<List<int>> islands, int[] droppedLoose, HashSet<int> removable, bool removesAll = false)
        {
            var replaced = new HashSet<int>();
            var made = new List<(int[] Corners, int Group, bool Smooth)>();

            foreach (var island in islands)
            {
                if (island.Count < 2 && !removesAll)
                    continue;

                if (mesh.BoundaryLoops(island) is not [var loop])
                    continue;

                replaced.UnionWith(island);
                made.Add((loop, mesh.Groups[island[0]], mesh.Smooth[island[0]]));
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (replaced.Contains(f))
                    continue;

                faces.Add(mesh.Faces[f]);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            foreach (var (corners, group, flat) in made)
            {
                faces.Add(corners);
                groups.Add(group);
                smooth.Add(flat);
            }

            var gone = new HashSet<int>(droppedLoose);
            var loose = mesh.LooseEdges.Where(edge => !gone.Contains(mesh.FindEdge(edge.A, edge.B))).ToList();
            var draft = new MeshTopology(mesh.Points, [.. faces], [.. groups], [.. smooth], mesh.GroupCount, loose);
            var straight = removable.Where(vertex => removesAll || IsInLine(draft, vertex)).ToHashSet();

            if (made.Count == 0 && gone.Count == 0 && straight.Count == 0)
                return null;

            var cleaned = Without(draft, straight);
            var added = cleaned.Faces.Skip(cleaned.Faces.Length - made.Count).SelectMany(face => face).ToHashSet();
            var (built, moved) = MeshPacking.Pack(cleaned.Points, cleaned.Faces, cleaned.Groups, cleaned.Smooth, cleaned.LooseEdges, vertex => mesh.IsIsolated(vertex) && !removable.Contains(vertex), mesh.GroupCount);

            return (built, moved.Moved(added));
        }

        private static bool Spreads(MeshTopology mesh, HashSet<int> chosen, int vertex) => chosen.Contains(vertex) && mesh.VertexEdges[vertex].Length > 2;

        private static bool IsInLine(MeshTopology mesh, int vertex)
        {
            if (vertex >= mesh.Points.Length || mesh.VertexEdges[vertex].Length != 2)
                return false;

            var here = mesh.Points[vertex];
            var first = mesh.Points[mesh.Edges[mesh.VertexEdges[vertex][0]].Other(vertex)] - here;
            var second = mesh.Points[mesh.Edges[mesh.VertexEdges[vertex][1]].Other(vertex)] - here;

            if (first.LengthSquared() < 1e-12f || second.LengthSquared() < 1e-12f)
                return true;

            return Vector3.Dot(Vector3.Normalize(first), Vector3.Normalize(second)) < -StraightCos;
        }

        private static MeshTopology Without(MeshTopology mesh, HashSet<int> vertices)
        {
            if (vertices.Count == 0)
                return mesh;

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f].Where(corner => !vertices.Contains(corner)).ToArray();

                if (corners.Length < 3)
                    continue;

                faces.Add(corners);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            var loose = new List<MeshEdge>();
            var joined = new Dictionary<int, List<int>>();

            foreach (var edge in mesh.LooseEdges)
            {
                var a = vertices.Contains(edge.A);
                var b = vertices.Contains(edge.B);

                if (!a && !b)
                {
                    loose.Add(edge);
                    continue;
                }

                var (middle, other) = a ? (edge.A, edge.B) : (edge.B, edge.A);

                if (a && b)
                    continue;

                if (!joined.TryGetValue(middle, out var list))
                    joined[middle] = list = [];

                list.Add(other);
            }

            foreach (var (_, ends) in joined)
            {
                if (ends.Count == 2 && ends[0] != ends[1])
                    loose.Add(new MeshEdge(ends[0], ends[1]));
            }

            return new MeshTopology(mesh.Points, [.. faces], [.. groups], [.. smooth], mesh.GroupCount, loose);
        }
    }
}
