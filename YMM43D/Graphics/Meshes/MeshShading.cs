using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshShading
    {
        public static SurfaceGeometry ToGeometry(this MeshTopology mesh, float? smoothAngle = null)
        {
            var faceNormals = new Vector3[mesh.Faces.Length];

            for (var f = 0; f < faceNormals.Length; f++)
                faceNormals[f] = mesh.NormalOf(f);

            var limit = smoothAngle is { } angle ? MathF.Cos(angle) : -2f;
            var starts = new int[mesh.Faces.Length + 1];

            for (var f = 0; f < mesh.Faces.Length; f++)
                starts[f + 1] = starts[f] + mesh.Faces[f].Length;

            var owner = Enumerable.Range(0, starts[^1]).ToArray();

            int Root(int corner)
            {
                while (owner[corner] != corner)
                    corner = owner[corner] = owner[owner[corner]];

                return corner;
            }

            int CornerOf(int face, int vertex) => starts[face] + Array.IndexOf(mesh.Faces[face], vertex);

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                if (mesh.EdgeFaces[e] is not [var first, var second]
                    || !mesh.Smooth[first]
                    || !mesh.Smooth[second]
                    || mesh.MarkOf(e).Sharp
                    || Vector3.Dot(faceNormals[first], faceNormals[second]) < limit)
                {
                    continue;
                }

                foreach (var vertex in new[] { mesh.Edges[e].A, mesh.Edges[e].B })
                {
                    var (a, b) = (Root(CornerOf(first, vertex)), Root(CornerOf(second, vertex)));

                    if (a != b)
                        owner[a] = b;
                }
            }

            var sums = new Dictionary<int, Vector3>();
            var all = new Vector3[mesh.Points.Length];

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f];

                for (var i = 0; i < corners.Length; i++)
                {
                    var weighted = faceNormals[f] * CornerAngle(mesh, corners, i);

                    all[corners[i]] += weighted;

                    if (mesh.Smooth[f])
                    {
                        var root = Root(starts[f] + i);

                        sums[root] = sums.GetValueOrDefault(root) + weighted;
                    }
                }
            }

            var uvs = mesh.UvsOrGenerated();
            var points = new List<Vector3>(mesh.Points);
            var normals = new List<Vector3>(all.Select(Unit));
            var claimed = new bool[mesh.Points.Length];
            var placed = new Dictionary<int, int>();
            var faces = new SurfaceFace[mesh.Faces.Length];

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f];
                var indices = new int[corners.Length];

                for (var i = 0; i < corners.Length; i++)
                {
                    var vertex = corners[i];

                    if (!mesh.Smooth[f])
                    {
                        indices[i] = vertex;
                        continue;
                    }

                    var root = Root(starts[f] + i);

                    if (!placed.TryGetValue(root, out var index))
                    {
                        if (!claimed[vertex])
                        {
                            index = vertex;
                            claimed[vertex] = true;
                            normals[vertex] = Unit(sums[root]);
                        }
                        else
                        {
                            index = points.Count;
                            points.Add(mesh.Points[vertex]);
                            normals.Add(Unit(sums[root]));
                        }

                        placed[root] = index;
                    }

                    indices[i] = index;
                }

                faces[f] = new SurfaceFace(indices, mesh.Groups[f], mesh.Smooth[f], uvs[f]);
            }

            return new SurfaceGeometry([.. points], [.. normals], faces, mesh.GroupCount);
        }

        private static float CornerAngle(MeshTopology mesh, int[] corners, int at)
        {
            var here = mesh.Points[corners[at]];
            var before = mesh.Points[corners[(at + corners.Length - 1) % corners.Length]] - here;
            var after = mesh.Points[corners[(at + 1) % corners.Length]] - here;

            if (before.LengthSquared() < 1e-20f || after.LengthSquared() < 1e-20f)
                return 0f;

            return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(before), Vector3.Normalize(after)), -1f, 1f));
        }

        private static Vector3 Unit(Vector3 sum) => sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : Vector3.UnitY;
    }
}
