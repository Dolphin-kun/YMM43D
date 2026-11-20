using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshUnwrap
    {
        private const float PackMargin = 0.02f;
        private const int RotationSteps = 30;
        private const int MaxIterations = 4000;
        private const float Tolerance = 1e-10f;
        private const float ClosedAngle = 66f;

        public static (MeshTopology Mesh, int[] Added)? Unwrapped(this MeshTopology mesh, IReadOnlyCollection<int> faces)
            => Apply(mesh, faces, chosen =>
            {
                bool Joins(int edge, int first, int second) => !mesh.MarkOf(edge).Seam;
                var islands = new List<Island>();

                foreach (var island in Islands(mesh, chosen, Joins))
                {
                    if (IsClosed(mesh, island, Joins))
                    {
                        islands.AddRange(Smart(mesh, [.. island.Faces], float.DegreesToRadians(ClosedAngle)));
                        continue;
                    }

                    Conformal(mesh, island);
                    islands.Add(island);
                }

                return islands;
            });

        public static (MeshTopology Mesh, int[] Added)? SmartProjected(this MeshTopology mesh, IReadOnlyCollection<int> faces, float angleLimit)
            => Apply(mesh, faces, chosen => Smart(mesh, chosen, angleLimit));

        public static (MeshTopology Mesh, int[] Added)? CubeProjected(this MeshTopology mesh, IReadOnlyCollection<int> faces)
            => Placed(mesh, faces, (face, corner, min, size) => MeshUv.BoxUv(mesh.Points[corner], mesh.NormalOf(face), min, size));

        public static (MeshTopology Mesh, int[] Added)? CylinderProjected(this MeshTopology mesh, IReadOnlyCollection<int> faces)
            => Wrapped(mesh, faces, (point, min, size) => new Vector2(MathF.Atan2(point.X, point.Z) / MathF.Tau + 0.5f, 1f - (point.Y - min.Y) / size.Y));

        public static (MeshTopology Mesh, int[] Added)? SphereProjected(this MeshTopology mesh, IReadOnlyCollection<int> faces)
            => Wrapped(mesh, faces, (point, _, _) =>
            {
                var length = MathF.Max(point.Length(), 1e-6f);

                return new Vector2(MathF.Atan2(point.X, point.Z) / MathF.Tau + 0.5f, 0.5f - MathF.Asin(Math.Clamp(point.Y / length, -1f, 1f)) / MathF.PI);
            });

        public static (MeshTopology Mesh, int[] Added)? ViewProjected(this MeshTopology mesh, IReadOnlyCollection<int> faces, Matrix4x4 toScreen)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length == 0)
                return null;

            var flat = chosen.SelectMany(face => mesh.Faces[face]).Distinct().ToDictionary(corner => corner, corner => Screen(mesh.Points[corner], toScreen));
            var (min, max) = (flat.Values.Aggregate(new Vector2(float.MaxValue), Vector2.Min), flat.Values.Aggregate(new Vector2(float.MinValue), Vector2.Max));
            var size = MathF.Max(MathF.Max(max.X - min.X, max.Y - min.Y), 1e-6f);

            return Written(mesh, chosen, (face, corner) => new Vector2((flat[corner].X - min.X) / size, 1f - (flat[corner].Y - min.Y) / size));
        }

        public static (MeshTopology Mesh, int[] Added)? ResetUvs(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length == 0)
                return null;

            var result = (Vector2[][])mesh.UvsOrGenerated().Clone();

            foreach (var face in chosen)
            {
                var n = mesh.Faces[face].Length;

                result[face] = n == 4
                    ? [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)]
                    : [.. Enumerable.Range(0, n).Select(i => new Vector2(0.5f + 0.5f * MathF.Cos(MathF.Tau * i / n), 0.5f + 0.5f * MathF.Sin(MathF.Tau * i / n)))];
            }

            return (mesh.WithUvs(result), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        public static (MeshTopology Mesh, int[] Added)? Packed(this MeshTopology mesh, IReadOnlyCollection<int> faces)
            => Apply(mesh, faces, chosen =>
            {
                var source = mesh.UvsOrGenerated();
                var islands = Islands(mesh, chosen, (edge, first, second) => Continuous(mesh, source, edge, first, second));

                foreach (var island in islands)
                {
                    foreach (var (face, corner) in island.Corners)
                        island.Uv[island.Vertex[(face, corner)]] = source[face][corner];
                }

                return islands;
            });

        private static (MeshTopology Mesh, int[] Added)? Apply(MeshTopology mesh, IReadOnlyCollection<int> faces, Func<int[], List<Island>> build)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length == 0)
                return null;

            var islands = build(chosen);

            Pack(mesh, islands);

            var result = (Vector2[][])mesh.UvsOrGenerated().Clone();

            foreach (var face in chosen)
                result[face] = (Vector2[])result[face].Clone();

            foreach (var island in islands)
            {
                foreach (var (face, corner) in island.Corners)
                    result[face][corner] = island.Uv[island.Vertex[(face, corner)]];
            }

            return (mesh.WithUvs(result), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        private static (MeshTopology Mesh, int[] Added)? Placed(MeshTopology mesh, IReadOnlyCollection<int> faces, Func<int, int, Vector3, Vector3, Vector2> place)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length == 0)
                return null;

            var (min, max) = MeshUv.Bounds(chosen.SelectMany(face => mesh.Faces[face]).Select(corner => mesh.Points[corner]));
            var size = Vector3.Max(max - min, new Vector3(1e-6f));

            return Written(mesh, chosen, (face, corner) => place(face, corner, min, size));
        }

        private static (MeshTopology Mesh, int[] Added)? Wrapped(MeshTopology mesh, IReadOnlyCollection<int> faces, Func<Vector3, Vector3, Vector3, Vector2> place)
        {
            var chosen = mesh.Existing(faces);

            if (chosen.Length == 0)
                return null;

            var (min, max) = MeshUv.Bounds(chosen.SelectMany(face => mesh.Faces[face]).Select(corner => mesh.Points[corner]));
            var size = Vector3.Max(max - min, new Vector3(1e-6f));
            var center = (min + max) / 2f;
            var result = (Vector2[][])mesh.UvsOrGenerated().Clone();

            foreach (var face in chosen)
            {
                var uvs = mesh.Faces[face].Select(corner => place(mesh.Points[corner] - center, min - center, size)).ToArray();
                var first = uvs[0].X;

                for (var i = 1; i < uvs.Length; i++)
                {
                    if (uvs[i].X - first > 0.5f)
                        uvs[i].X -= 1f;
                    else if (first - uvs[i].X > 0.5f)
                        uvs[i].X += 1f;
                }

                result[face] = uvs;
            }

            return (mesh.WithUvs(result), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        private static (MeshTopology Mesh, int[] Added) Written(MeshTopology mesh, int[] chosen, Func<int, int, Vector2> place)
        {
            var result = (Vector2[][])mesh.UvsOrGenerated().Clone();

            foreach (var face in chosen)
                result[face] = [.. mesh.Faces[face].Select(corner => place(face, corner))];

            return (mesh.WithUvs(result), [.. chosen.SelectMany(face => mesh.Faces[face]).Distinct()]);
        }

        private static Vector2 Screen(Vector3 point, Matrix4x4 toScreen)
        {
            var clip = Vector4.Transform(new Vector4(point, 1f), toScreen);

            return MathF.Abs(clip.W) < 1e-8f ? Vector2.Zero : new Vector2(clip.X / clip.W, clip.Y / clip.W);
        }

        private static float FaceArea(MeshTopology mesh, int face)
        {
            var corners = mesh.Faces[face];
            var sum = Vector3.Zero;

            for (var i = 1; i + 1 < corners.Length; i++)
                sum += Vector3.Cross(mesh.Points[corners[i]] - mesh.Points[corners[0]], mesh.Points[corners[i + 1]] - mesh.Points[corners[0]]);

            return sum.Length() / 2f;
        }

        private static bool Continuous(MeshTopology mesh, Vector2[][] uvs, int edge, int first, int second)
        {
            foreach (var vertex in new[] { mesh.Edges[edge].A, mesh.Edges[edge].B })
            {
                var a = uvs[first][Array.IndexOf(mesh.Faces[first], vertex)];
                var b = uvs[second][Array.IndexOf(mesh.Faces[second], vertex)];

                if (Vector2.DistanceSquared(a, b) > 1e-10f)
                    return false;
            }

            return true;
        }

        private static List<Island> Smart(MeshTopology mesh, int[] chosen, float angleLimit)
        {
            var limit = MathF.Cos(angleLimit);
            var islands = new List<Island>();
            var left = new HashSet<int>(chosen);

            while (left.Count > 0)
            {
                var seed = left.First();
                var direction = mesh.NormalOf(seed);
                var group = new List<int>();
                var queue = new Queue<int>([seed]);

                left.Remove(seed);

                while (queue.Count > 0)
                {
                    var face = queue.Dequeue();

                    group.Add(face);

                    foreach (var edge in mesh.FaceEdges[face])
                    {
                        foreach (var other in mesh.EdgeFaces[edge])
                        {
                            if (left.Contains(other) && Vector3.Dot(mesh.NormalOf(other), direction) >= limit)
                            {
                                left.Remove(other);
                                queue.Enqueue(other);
                            }
                        }
                    }
                }

                var island = Island.Of(mesh, group, (_, _, _) => true);
                var normal = group.Aggregate(Vector3.Zero, (sum, face) => sum + mesh.NormalOf(face) * FaceArea(mesh, face));

                Project(mesh, island, normal);
                islands.Add(island);
            }

            return islands;
        }

        private static bool IsClosed(MeshTopology mesh, Island island, Func<int, int, int, bool> joins)
        {
            var inside = new HashSet<int>(island.Faces);

            return island.Faces.All(face => mesh.FaceEdges[face].All(edge =>
                mesh.EdgeFaces[edge] is [var first, var second] && inside.Contains(first) && inside.Contains(second) && joins(edge, first, second)));
        }

        private static List<Island> Islands(MeshTopology mesh, int[] chosen, Func<int, int, int, bool> joins)
        {
            var inside = new HashSet<int>(chosen);
            var islands = new List<Island>();
            var left = new HashSet<int>(chosen);

            while (left.Count > 0)
            {
                var seed = left.First();
                var group = new List<int>();
                var queue = new Queue<int>([seed]);

                left.Remove(seed);

                while (queue.Count > 0)
                {
                    var face = queue.Dequeue();

                    group.Add(face);

                    foreach (var edge in mesh.FaceEdges[face])
                    {
                        foreach (var other in mesh.EdgeFaces[edge])
                        {
                            if (other != face && inside.Contains(other) && left.Contains(other) && joins(edge, face, other))
                            {
                                left.Remove(other);
                                queue.Enqueue(other);
                            }
                        }
                    }
                }

                islands.Add(Island.Of(mesh, group, joins));
            }

            return islands;
        }

        private static void Project(MeshTopology mesh, Island island, Vector3 normal)
        {
            var (right, up) = MeshUv.Basis(normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ);

            foreach (var (face, corner) in island.Corners)
            {
                var point = mesh.Points[mesh.Faces[face][corner]];

                island.Uv[island.Vertex[(face, corner)]] = new Vector2(Vector3.Dot(point, right), -Vector3.Dot(point, up));
            }
        }

        private static void Conformal(MeshTopology mesh, Island island)
        {
            var count = island.Uv.Length;

            if (count < 3)
            {
                Project(mesh, island, island.Faces.Aggregate(Vector3.Zero, (sum, face) => sum + mesh.NormalOf(face)));
                return;
            }

            var points = new Vector3[count];

            foreach (var ((face, corner), index) in island.Vertex)
                points[index] = mesh.Points[mesh.Faces[face][corner]];

            var (pinA, pinB) = FarthestPair(points);
            var rows = new List<(int Index, float Value)[]>();

            foreach (var face in island.Faces)
            {
                var corners = mesh.Faces[face];

                for (var i = 1; i + 1 < corners.Length; i++)
                {
                    int[] ids = [island.Vertex[(face, 0)], island.Vertex[(face, i)], island.Vertex[(face, i + 1)]];
                    var (p0, p1, p2) = (points[ids[0]], points[ids[1]], points[ids[2]]);
                    var axisX = p1 - p0;
                    var normal = Vector3.Cross(axisX, p2 - p0);
                    var area = normal.Length() / 2f;

                    if (area < 1e-12f || axisX.LengthSquared() < 1e-20f)
                        continue;

                    axisX = Vector3.Normalize(axisX);

                    var axisY = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(normal), axisX));
                    Vector2[] flat = [Vector2.Zero, new((p1 - p0).Length(), 0f), new(Vector3.Dot(p2 - p0, axisX), Vector3.Dot(p2 - p0, axisY))];
                    var weight = 1f / MathF.Sqrt(2f * area);
                    var real = new (int, float)[6];
                    var imaginary = new (int, float)[6];

                    for (var j = 0; j < 3; j++)
                    {
                        var (k, l) = (flat[(j + 2) % 3], flat[(j + 1) % 3]);
                        var (wr, wi) = ((k.X - l.X) * weight, (k.Y - l.Y) * weight);

                        real[j * 2] = (ids[j] * 2, wr);
                        real[j * 2 + 1] = (ids[j] * 2 + 1, -wi);
                        imaginary[j * 2] = (ids[j] * 2, wi);
                        imaginary[j * 2 + 1] = (ids[j] * 2 + 1, wr);
                    }

                    rows.Add(real);
                    rows.Add(imaginary);
                }
            }

            var pinned = new Dictionary<int, float> { [pinA * 2] = 0f, [pinA * 2 + 1] = 0f, [pinB * 2] = 1f, [pinB * 2 + 1] = 0f };
            var solution = LeastSquares(rows, count * 2, pinned);

            for (var i = 0; i < count; i++)
                island.Uv[i] = new Vector2(solution[i * 2], solution[i * 2 + 1]);

            if (SignedArea(mesh, island) > 0f)
            {
                for (var i = 0; i < count; i++)
                    island.Uv[i] = new Vector2(-island.Uv[i].X, island.Uv[i].Y);
            }
        }

        private static float[] LeastSquares(List<(int Index, float Value)[]> rows, int size, Dictionary<int, float> pinned)
        {
            var x = new float[size];

            foreach (var (index, value) in pinned)
                x[index] = value;

            var target = new float[rows.Count];

            for (var r = 0; r < rows.Count; r++)
            {
                foreach (var (index, value) in rows[r])
                {
                    if (pinned.TryGetValue(index, out var fixedValue))
                        target[r] -= value * fixedValue;
                }
            }

            float[] Normal(float[] vector)
            {
                var result = new float[size];

                foreach (var row in rows)
                {
                    var dot = 0f;

                    foreach (var (index, value) in row)
                    {
                        if (!pinned.ContainsKey(index))
                            dot += value * vector[index];
                    }

                    foreach (var (index, value) in row)
                    {
                        if (!pinned.ContainsKey(index))
                            result[index] += value * dot;
                    }
                }

                return result;
            }

            var b = new float[size];

            for (var r = 0; r < rows.Count; r++)
            {
                foreach (var (index, value) in rows[r])
                {
                    if (!pinned.ContainsKey(index))
                        b[index] += value * target[r];
                }
            }

            var free = new float[size];
            var residual = (float[])b.Clone();
            var direction = (float[])residual.Clone();
            var old = Dot(residual, residual);

            for (var iteration = 0; iteration < MaxIterations && old > Tolerance; iteration++)
            {
                var step = Normal(direction);
                var curve = Dot(direction, step);

                if (MathF.Abs(curve) < 1e-30f)
                    break;

                var alpha = old / curve;

                for (var i = 0; i < size; i++)
                {
                    free[i] += alpha * direction[i];
                    residual[i] -= alpha * step[i];
                }

                var fresh = Dot(residual, residual);

                for (var i = 0; i < size; i++)
                    direction[i] = residual[i] + fresh / old * direction[i];

                old = fresh;
            }

            for (var i = 0; i < size; i++)
            {
                if (!pinned.ContainsKey(i))
                    x[i] = free[i];
            }

            return x;
        }

        private static float Dot(float[] a, float[] b)
        {
            var sum = 0f;

            for (var i = 0; i < a.Length; i++)
                sum += a[i] * b[i];

            return sum;
        }

        private static (int, int) FarthestPair(Vector3[] points)
        {
            var first = 0;
            var far = 0f;

            for (var i = 0; i < points.Length; i++)
            {
                var distance = Vector3.DistanceSquared(points[0], points[i]);

                if (distance > far)
                {
                    far = distance;
                    first = i;
                }
            }

            var second = first;

            far = 0f;

            for (var i = 0; i < points.Length; i++)
            {
                var distance = Vector3.DistanceSquared(points[first], points[i]);

                if (distance > far)
                {
                    far = distance;
                    second = i;
                }
            }

            return (first, second == first ? (first + 1) % points.Length : second);
        }

        private static float SignedArea(MeshTopology mesh, Island island)
        {
            var sum = 0f;

            foreach (var face in island.Faces)
            {
                var n = mesh.Faces[face].Length;

                for (var i = 0; i < n; i++)
                {
                    var (a, b) = (island.Uv[island.Vertex[(face, i)]], island.Uv[island.Vertex[(face, (i + 1) % n)]]);

                    sum += a.X * b.Y - b.X * a.Y;
                }
            }

            return -sum;
        }

        private static void Pack(MeshTopology mesh, List<Island> islands)
        {
            if (islands.Count == 0)
                return;

            var boxes = new List<(Island Island, Vector2 Min, Vector2 Size)>();

            foreach (var island in islands)
            {
                var uvArea = MathF.Abs(SignedArea(mesh, island)) / 2f;
                var area = island.Faces.Sum(face => FaceArea(mesh, face));
                var scale = uvArea > 1e-12f ? MathF.Sqrt(area / uvArea) : 1f;

                for (var i = 0; i < island.Uv.Length; i++)
                    island.Uv[i] *= scale;

                Upright(island);

                var (min, max) = (island.Uv.Aggregate(new Vector2(float.MaxValue), Vector2.Min), island.Uv.Aggregate(new Vector2(float.MinValue), Vector2.Max));

                boxes.Add((island, min, max - min));
            }

            var margin = boxes.Max(box => MathF.Max(box.Size.X, box.Size.Y)) * PackMargin;
            var total = boxes.Sum(box => (box.Size.X + margin) * (box.Size.Y + margin));
            var width = MathF.Max(MathF.Sqrt(total), boxes.Max(box => box.Size.X + margin));
            var (x, y, row) = (0f, 0f, 0f);
            var placed = new List<(Island Island, Vector2 Shift)>();

            foreach (var (island, min, size) in boxes.OrderByDescending(box => box.Size.Y))
            {
                if (x > 0f && x + size.X > width)
                {
                    x = 0f;
                    y += row + margin;
                    row = 0f;
                }

                placed.Add((island, new Vector2(x, y) - min));
                x += size.X + margin;
                row = MathF.Max(row, size.Y);
            }

            var extent = MathF.Max(width, y + row);
            var fit = extent > 1e-12f ? (1f - PackMargin * 2f) / extent : 1f;

            foreach (var (island, shift) in placed)
            {
                for (var i = 0; i < island.Uv.Length; i++)
                {
                    island.Uv[i] = (island.Uv[i] + shift) * fit + new Vector2(PackMargin);
                }
            }
        }

        private static void Upright(Island island)
        {
            var best = 0f;
            var smallest = float.MaxValue;

            for (var step = 0; step < RotationSteps; step++)
            {
                var angle = MathF.PI / 2f * step / RotationSteps;
                var (sin, cos) = MathF.SinCos(angle);
                var min = new Vector2(float.MaxValue);
                var max = new Vector2(float.MinValue);

                foreach (var uv in island.Uv)
                {
                    var turned = new Vector2(uv.X * cos - uv.Y * sin, uv.X * sin + uv.Y * cos);

                    min = Vector2.Min(min, turned);
                    max = Vector2.Max(max, turned);
                }

                var area = (max.X - min.X) * (max.Y - min.Y);

                if (area < smallest)
                {
                    smallest = area;
                    best = angle;
                }
            }

            var (s, c) = MathF.SinCos(best);

            for (var i = 0; i < island.Uv.Length; i++)
                island.Uv[i] = new Vector2(island.Uv[i].X * c - island.Uv[i].Y * s, island.Uv[i].X * s + island.Uv[i].Y * c);
        }

        private sealed class Island
        {
            public required List<int> Faces { get; init; }

            public required Dictionary<(int Face, int Corner), int> Vertex { get; init; }

            public required Vector2[] Uv { get; init; }

            public IEnumerable<(int Face, int Corner)> Corners => Vertex.Keys;

            public static Island Of(MeshTopology mesh, List<int> faces, Func<int, int, int, bool> joins)
            {
                var inside = new HashSet<int>(faces);
                var owner = new Dictionary<(int, int), (int, int)>();

                (int, int) Root((int, int) corner)
                {
                    while (owner[corner] != corner)
                        corner = owner[corner] = owner[owner[corner]];

                    return corner;
                }

                foreach (var face in faces)
                {
                    for (var i = 0; i < mesh.Faces[face].Length; i++)
                        owner[(face, i)] = (face, i);
                }

                foreach (var face in faces)
                {
                    foreach (var edge in mesh.FaceEdges[face])
                    {
                        if (mesh.EdgeFaces[edge] is not [var first, var second] || !inside.Contains(first) || !inside.Contains(second) || !joins(edge, first, second))
                            continue;

                        foreach (var vertex in new[] { mesh.Edges[edge].A, mesh.Edges[edge].B })
                        {
                            var (a, b) = (Root((first, Array.IndexOf(mesh.Faces[first], vertex))), Root((second, Array.IndexOf(mesh.Faces[second], vertex))));

                            if (a != b)
                                owner[a] = b;
                        }
                    }
                }

                var numbers = new Dictionary<(int, int), int>();
                var numbered = new Dictionary<(int Face, int Corner), int>();

                foreach (var corner in owner.Keys.ToArray())
                {
                    var root = Root(corner);

                    if (!numbers.TryGetValue(root, out var index))
                        numbers[root] = index = numbers.Count;

                    numbered[corner] = index;
                }

                return new Island { Faces = faces, Vertex = numbered, Uv = new Vector2[numbers.Count] };
            }
        }
    }
}
