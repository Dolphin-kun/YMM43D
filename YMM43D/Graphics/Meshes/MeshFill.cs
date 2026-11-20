using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshFill
    {
        public static (MeshTopology Mesh, int[] Added)? Fill(this MeshTopology mesh, IReadOnlyCollection<int> vertices, IReadOnlyCollection<int> edges)
        {
            var points = mesh.ExistingPoints(vertices);

            if (points.Length == 2)
            {
                if (mesh.FindEdge(points[0], points[1]) >= 0)
                    return null;

                return (new MeshTopology(mesh.Points, mesh.Faces, mesh.Groups, mesh.Smooth, mesh.GroupCount, [.. mesh.LooseEdges, new MeshEdge(points[0], points[1])], mesh.Marks, mesh.Uvs, mesh.Keys, mesh.Weights), points);
            }

            if (points.Length < 3)
                return null;

            var ring = Ring(mesh, points, mesh.ExistingEdges(edges));

            if (ring is null || HasFace(mesh, ring))
                return null;

            return (mesh.With(mesh.Points, [.. mesh.Faces, ring], [.. mesh.Groups, 0], [.. mesh.Smooth, false]), ring);
        }

        public static (MeshTopology Mesh, int[] Added)? GridFill(this MeshTopology mesh, IReadOnlyCollection<int> edges, int span, int offset)
        {
            if (mesh.Ordered(mesh.ExistingEdges(edges)) is not ({ } order, true) || order.Length < 4 || order.Length % 2 != 0)
                return null;

            var loop = mesh.Oriented(order);
            var count = loop.Length;
            var half = count / 2;
            var across = Math.Clamp(span, 1, half - 1);
            var along = half - across;
            var start = ((offset % count) + count) % count;

            int At(int index) => loop[(start + index) % count];

            var grid = new int[across + 1, along + 1];

            for (var i = 0; i <= across; i++)
            {
                grid[i, 0] = At(i);
                grid[i, along] = At(across + along + (across - i));
            }

            for (var j = 0; j <= along; j++)
            {
                grid[across, j] = At(across + j);
                grid[0, j] = At((2 * across + along + (along - j)) % count);
            }

            var points = new List<Vector3>(mesh.Points);
            var added = new List<int>(loop);

            Vector3 P(int i, int j) => points[grid[i, j]];

            for (var i = 1; i < across; i++)
            {
                for (var j = 1; j < along; j++)
                {
                    var u = (float)i / across;
                    var v = (float)j / along;
                    var place = (1f - v) * P(i, 0) + v * P(i, along) + (1f - u) * P(0, j) + u * P(across, j)
                        - ((1f - u) * (1f - v) * P(0, 0) + u * (1f - v) * P(across, 0) + (1f - u) * v * P(0, along) + u * v * P(across, along));

                    grid[i, j] = points.Count;
                    added.Add(points.Count);
                    points.Add(place);
                }
            }

            var faces = new List<int[]>(mesh.Faces);
            var groups = new List<int>(mesh.Groups);
            var smooth = new List<bool>(mesh.Smooth);

            for (var i = 0; i < across; i++)
            {
                for (var j = 0; j < along; j++)
                {
                    faces.Add([grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]]);
                    groups.Add(0);
                    smooth.Add(false);
                }
            }

            return (mesh.With([.. points], faces, groups, smooth), [.. added]);
        }

        public static int DefaultSpan(this MeshTopology mesh, IReadOnlyCollection<int> edges)
        {
            var count = mesh.Ordered(mesh.ExistingEdges(edges)) is ({ } order, true) ? order.Length : 0;

            return Math.Max(1, count / 4);
        }

        private static int[]? Ring(MeshTopology mesh, int[] points, int[] edges)
        {
            var chosen = new HashSet<int>(points);
            var along = edges.Where(edge => chosen.Contains(mesh.Edges[edge].A) && chosen.Contains(mesh.Edges[edge].B)).ToArray();

            if (along.Length > 0 && mesh.Ordered(along) is ({ } order, _) && order.Length == points.Length)
                return mesh.Oriented(order);

            return Around(mesh, points);
        }

        private static bool HasFace(MeshTopology mesh, int[] ring)
        {
            var key = new HashSet<int>(ring);

            return mesh.Faces.Any(face => face.Length == ring.Length && face.All(key.Contains));
        }

        private static int[]? Around(MeshTopology mesh, int[] chosen)
        {
            var middle = Vector3.Zero;

            foreach (var one in chosen)
                middle += mesh.Points[one];

            middle /= chosen.Length;

            var normal = Vector3.Zero;

            for (var i = 0; i < chosen.Length; i++)
            {
                var here = mesh.Points[chosen[i]] - middle;
                var next = mesh.Points[chosen[(i + 1) % chosen.Length]] - middle;

                normal += Vector3.Cross(here, next);
            }

            if (normal.LengthSquared() < 1e-14f)
            {
                for (var i = 0; i < chosen.Length; i++)
                {
                    for (var j = i + 1; j < chosen.Length; j++)
                    {
                        var turn = Vector3.Cross(mesh.Points[chosen[i]] - middle, mesh.Points[chosen[j]] - middle);

                        if (turn.LengthSquared() > normal.LengthSquared())
                            normal = turn;
                    }
                }
            }

            if (normal.LengthSquared() < 1e-14f)
                return null;

            normal = Vector3.Normalize(normal);

            var guess = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(guess, normal));
            var up = Vector3.Cross(normal, right);

            int[] ordered =
            [
                .. chosen
                    .OrderByDescending(one => MathF.Atan2(Vector3.Dot(mesh.Points[one] - middle, up), Vector3.Dot(mesh.Points[one] - middle, right))),
            ];

            return mesh.Oriented(ordered);
        }
    }
}
