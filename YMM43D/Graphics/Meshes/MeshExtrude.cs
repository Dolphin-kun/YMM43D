using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshExtrude
    {
        public static (MeshTopology Mesh, int[] Added)? Extrude(this MeshTopology mesh, IReadOnlyCollection<int> faces, IReadOnlyCollection<int> edges, IReadOnlyCollection<int> vertices)
            => mesh.Extruded(faces, edges, vertices) is { } made ? (made.Mesh, [.. made.Copies.Values]) : null;

        public static (MeshTopology Mesh, int[] Added)? ExtrudeAlongNormals(this MeshTopology mesh, IReadOnlyCollection<int> faces, float distance)
        {
            var chosen = mesh.Existing(faces);

            if (mesh.Extruded(chosen, [], []) is not { } made)
                return null;

            var normals = new Dictionary<int, Vector3>();

            foreach (var face in chosen)
            {
                var normal = mesh.NormalOf(face);

                foreach (var corner in mesh.Faces[face])
                    normals[corner] = normals.GetValueOrDefault(corner) + normal;
            }

            var points = (Vector3[])made.Mesh.Points.Clone();

            foreach (var (from, copy) in made.Copies)
            {
                if (normals.TryGetValue(from, out var normal) && normal.LengthSquared() > 1e-12f)
                    points[copy] += Vector3.Normalize(normal) * distance;
            }

            return (made.Mesh.WithPoints(points), [.. made.Copies.Values]);
        }

        public static (MeshTopology Mesh, int[] Added)? ExtrudeIndividual(this MeshTopology mesh, IReadOnlyCollection<int> faces, float distance)
        {
            var chosen = new HashSet<int>(mesh.Existing(faces));

            if (chosen.Count == 0)
                return null;

            var points = new List<Vector3>(mesh.Points);
            var made = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();
            var added = new List<int>();
            var sides = new List<(int[] Corners, int Group)>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!chosen.Contains(f))
                {
                    made.Add(mesh.Faces[f]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);
                    continue;
                }

                var corners = mesh.Faces[f];
                var offset = mesh.NormalOf(f) * distance;
                var top = new int[corners.Length];

                for (var i = 0; i < corners.Length; i++)
                {
                    top[i] = points.Count;
                    added.Add(points.Count);
                    points.Add(mesh.Points[corners[i]] + offset);
                }

                made.Add(top);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);

                for (var i = 0; i < corners.Length; i++)
                {
                    var next = (i + 1) % corners.Length;

                    sides.Add(([corners[i], corners[next], top[next], top[i]], mesh.Groups[f]));
                }
            }

            foreach (var (corners, group) in sides)
            {
                made.Add(corners);
                groups.Add(group);
                smooth.Add(false);
            }

            return (mesh.With([.. points], made, groups, smooth), [.. added]);
        }

        public static (MeshTopology Mesh, Dictionary<int, int> Copies)? Extruded(this MeshTopology mesh, IReadOnlyCollection<int> chosenFaces, IReadOnlyCollection<int> chosenEdges, IReadOnlyCollection<int> chosenVertices)
        {
            var (faces, edges, lone) = mesh.Parted(chosenFaces, chosenEdges, chosenVertices);
            var onFace = new HashSet<int>(faces);

            if (faces.Length == 0 && edges.Length == 0 && lone.Length == 0)
                return null;

            var points = new List<Vector3>(mesh.Points);
            var copies = new Dictionary<int, int>();

            int Copy(int vertex)
            {
                if (!copies.TryGetValue(vertex, out var copy))
                {
                    copy = points.Count;
                    copies[vertex] = copy;
                    points.Add(mesh.Points[vertex]);
                }

                return copy;
            }

            var kept = new HashSet<int>();

            foreach (var island in mesh.Islands(faces))
            {
                var sealedIsland = island.SelectMany(face => mesh.FaceEdges[face]).All(edge => mesh.EdgeFaces[edge].All(onFace.Contains));

                if (sealedIsland)
                    kept.UnionWith(island);
            }

            var made = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (onFace.Contains(f) && kept.Contains(f))
                {
                    made.Add([.. mesh.Faces[f].Reverse()]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);
                }

                made.Add(onFace.Contains(f) ? [.. mesh.Faces[f].Select(Copy)] : mesh.Faces[f]);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            foreach (var f in faces)
            {
                var corners = mesh.Faces[f];

                for (var i = 0; i < corners.Length; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % corners.Length];
                    var edge = mesh.FindEdge(a, b);

                    if (mesh.EdgeFaces[edge].Count(onFace.Contains) != 1)
                        continue;

                    made.Add([a, b, Copy(b), Copy(a)]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(false);
                }
            }

            foreach (var edge in edges)
            {
                var (a, b) = (mesh.Edges[edge].A, mesh.Edges[edge].B);

                if (mesh.EdgeFaces[edge] is [var only] && mesh.Faces[only].Runs(a, b))
                    (a, b) = (b, a);

                made.Add([a, b, Copy(b), Copy(a)]);
                groups.Add(mesh.EdgeFaces[edge] is [var face] ? mesh.Groups[face] : 0);
                smooth.Add(false);
            }

            MeshEdge[] loose = [.. mesh.LooseEdges, .. lone.Select(vertex => new MeshEdge(vertex, Copy(vertex)))];

            return (new MeshTopology([.. points], [.. made], [.. groups], [.. smooth], mesh.GroupCount, loose), copies);
        }
    }
}
