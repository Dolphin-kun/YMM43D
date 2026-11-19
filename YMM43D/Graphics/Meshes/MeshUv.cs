using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshUv
    {
        private const float Weld = 1e-4f;
        private const float Flat = 1e-3f;
        private const float SameSide = 0.9f;

        public static Vector2[][] Generated(this MeshTopology mesh)
        {
            var (min, max) = Bounds(mesh.Points);
            var size = Vector3.Max(max - min, new Vector3(1e-6f));
            var uvs = new Vector2[mesh.Faces.Length][];

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var normal = mesh.NormalOf(f);
                uvs[f] = [.. mesh.Faces[f].Select(corner => BoxUv(mesh.Points[corner], normal, min, size))];
            }

            return uvs;
        }

        public static Vector2 BoxUv(Vector3 point, Vector3 normal, Vector3 min, Vector3 size)
        {
            var local = (point - min) / size;
            var (x, y, z) = (MathF.Abs(normal.X), MathF.Abs(normal.Y), MathF.Abs(normal.Z));

            if (x >= y && x >= z)
                return new Vector2(normal.X >= 0f ? 1f - local.Z : local.Z, 1f - local.Y);

            if (y >= z)
                return new Vector2(local.X, normal.Y >= 0f ? local.Z : 1f - local.Z);

            return new Vector2(normal.Z >= 0f ? local.X : 1f - local.X, 1f - local.Y);
        }

        public static Vector2[][] UvsOrGenerated(this MeshTopology mesh) => mesh.Uvs ?? mesh.Generated();

        public static Vector2[][]? Carried(MeshTopology to, MeshTopology from)
        {
            if (from.Uvs is not { } source)
                return null;

            var grid = new FaceGrid(from);
            var corners = new Dictionary<(long, long, long), List<(int Face, int Corner)>>();

            for (var f = 0; f < from.Faces.Length; f++)
            {
                for (var i = 0; i < from.Faces[f].Length; i++)
                {
                    var key = KeyOf(from.Points[from.Faces[f][i]]);

                    if (!corners.TryGetValue(key, out var list))
                        corners[key] = list = [];

                    list.Add((f, i));
                }
            }

            var result = new Vector2[to.Faces.Length][];

            for (var f = 0; f < to.Faces.Length; f++)
            {
                var face = to.Faces[f];
                var normal = to.NormalOf(f);
                var center = to.Middle(face);
                var host = grid.Host(center, normal);
                var uvs = new Vector2[face.Length];

                for (var i = 0; i < face.Length; i++)
                {
                    var point = to.Points[face[i]];

                    uvs[i] = host >= 0
                        ? Interpolate(from, source, host, point)
                        : Matching(from, source, corners, point, normal) ?? Nearest(from, source, point);
                }

                result[f] = uvs;
            }

            return result;
        }

        public static Vector2 Interpolate(MeshTopology mesh, Vector2[][] uvs, int face, Vector3 point)
        {
            var weights = FaceWeights(mesh, face, point);
            var uv = Vector2.Zero;

            for (var i = 0; i < weights.Length; i++)
                uv += uvs[face][i] * weights[i];

            return uv;
        }

        public static Vector3[][]? CarriedKeys(MeshTopology to, MeshTopology from)
        {
            if (from.Keys is not { } keys)
                return null;

            var places = new Dictionary<(long, long, long), int>();

            for (var i = 0; i < from.Points.Length; i++)
                places.TryAdd(KeyOf(from.Points[i]), i);

            FaceGrid? grid = null;
            var result = keys.Select(_ => new Vector3[to.Points.Length]).ToArray();

            for (var i = 0; i < to.Points.Length; i++)
            {
                var point = to.Points[i];
                var blend = places.TryGetValue(KeyOf(point), out var same)
                    ? [(same, 1f)]
                    : AlongEdge(from, point) ?? WithinFace(from, grid ??= new FaceGrid(from), point) ?? [(NearestPoint(from, point), 1f)];

                for (var k = 0; k < keys.Length; k++)
                {
                    var offset = Vector3.Zero;

                    foreach (var (index, weight) in blend)
                        offset += keys[k][index] * weight;

                    result[k][i] = offset;
                }
            }

            return result;
        }

        private static (int Index, float Weight)[]? AlongEdge(MeshTopology from, Vector3 point)
        {
            foreach (var edge in from.Edges)
            {
                var (a, b) = (from.Points[edge.A], from.Points[edge.B]);
                var span = b - a;
                var length = span.LengthSquared();

                if (length < 1e-20f)
                    continue;

                var t = Vector3.Dot(point - a, span) / length;

                if (t is < 0f or > 1f || Vector3.DistanceSquared(point, a + span * t) > Weld * Weld * MathF.Max(1f, length))
                    continue;

                return [(edge.A, 1f - t), (edge.B, t)];
            }

            return null;
        }

        private static (int Index, float Weight)[]? WithinFace(MeshTopology from, FaceGrid grid, Vector3 point)
        {
            var host = grid.Host(point, Vector3.Zero);

            if (host < 0)
                return null;

            var weights = FaceWeights(from, host, point);

            return [.. from.Faces[host].Select((corner, i) => (corner, weights[i]))];
        }

        private static int NearestPoint(MeshTopology from, Vector3 point)
        {
            var best = 0;
            var distance = float.MaxValue;

            for (var i = 0; i < from.Points.Length; i++)
            {
                var here = Vector3.DistanceSquared(from.Points[i], point);

                if (here < distance)
                    (best, distance) = (i, here);
            }

            return best;
        }

        private static float[] FaceWeights(MeshTopology mesh, int face, Vector3 point)
        {
            var corners = mesh.Faces[face];
            var (right, up) = Basis(mesh.NormalOf(face));
            var flat = new Vector2[corners.Length];
            var at = new Vector2(Vector3.Dot(point, right), Vector3.Dot(point, up));

            for (var i = 0; i < corners.Length; i++)
                flat[i] = new Vector2(Vector3.Dot(mesh.Points[corners[i]], right), Vector3.Dot(mesh.Points[corners[i]], up)) - at;

            return MeanValue(flat);
        }

        public static (Vector3 Right, Vector3 Up) Basis(Vector3 normal)
        {
            if (normal.LengthSquared() < 1e-12f)
                normal = Vector3.UnitZ;

            var guess = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(guess, normal));

            return (right, Vector3.Cross(normal, right));
        }

        public static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<Vector3> points)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var point in points)
            {
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            return min.X > max.X ? (Vector3.Zero, Vector3.Zero) : (min, max);
        }

        private static float[] MeanValue(Vector2[] offsets)
        {
            var n = offsets.Length;
            var weights = new float[n];

            for (var i = 0; i < n; i++)
            {
                if (offsets[i].LengthSquared() < 1e-14f)
                {
                    weights[i] = 1f;
                    return weights;
                }
            }

            for (var i = 0; i < n; i++)
            {
                var (a, b) = (offsets[i], offsets[(i + 1) % n]);
                var span = b - a;
                var length = span.LengthSquared();
                var t = length < 1e-20f ? 0f : Math.Clamp(-Vector2.Dot(a, span) / length, 0f, 1f);

                if ((a + span * t).LengthSquared() < 1e-14f)
                {
                    weights[i] = 1f - t;
                    weights[(i + 1) % n] = t;
                    return weights;
                }
            }

            var total = 0f;

            for (var i = 0; i < n; i++)
            {
                var previous = offsets[(i + n - 1) % n];
                var here = offsets[i];
                var next = offsets[(i + 1) % n];
                var weight = (HalfTan(previous, here) + HalfTan(here, next)) / here.Length();

                weights[i] = weight;
                total += weight;
            }

            if (MathF.Abs(total) < 1e-12f || !float.IsFinite(total))
            {
                Array.Fill(weights, 1f / n);
                return weights;
            }

            for (var i = 0; i < n; i++)
                weights[i] /= total;

            return weights;
        }

        private static float HalfTan(Vector2 a, Vector2 b)
        {
            var cross = a.X * b.Y - a.Y * b.X;
            var dot = Vector2.Dot(a, b);
            var angle = MathF.Atan2(cross, dot);

            return MathF.Tan(angle / 2f);
        }

        private static Vector2? Matching(MeshTopology from, Vector2[][] uvs, Dictionary<(long, long, long), List<(int Face, int Corner)>> corners, Vector3 point, Vector3 normal)
        {
            if (!corners.TryGetValue(KeyOf(point), out var list))
                return null;

            var best = list.MaxBy(one => Vector3.Dot(from.NormalOf(one.Face), normal));

            return uvs[best.Face][best.Corner];
        }

        private static Vector2 Nearest(MeshTopology from, Vector2[][] uvs, Vector3 point)
        {
            var best = (Face: 0, Corner: 0);
            var distance = float.MaxValue;

            for (var f = 0; f < from.Faces.Length; f++)
            {
                for (var i = 0; i < from.Faces[f].Length; i++)
                {
                    var here = Vector3.DistanceSquared(from.Points[from.Faces[f][i]], point);

                    if (here < distance)
                    {
                        distance = here;
                        best = (f, i);
                    }
                }
            }

            return from.Faces.Length == 0 ? Vector2.Zero : uvs[best.Face][best.Corner];
        }

        private static (long, long, long) KeyOf(in Vector3 point)
            => ((long)MathF.Round(point.X / Weld), (long)MathF.Round(point.Y / Weld), (long)MathF.Round(point.Z / Weld));

        private sealed class FaceGrid
        {
            private const int MaxCellsPerFace = 4096;

            private readonly MeshTopology mesh;
            private readonly Dictionary<(int, int, int), List<int>> cells = [];
            private readonly float cell;
            private readonly float tolerance;

            public FaceGrid(MeshTopology mesh)
            {
                this.mesh = mesh;

                var (min, max) = Bounds(mesh.Points);
                var span = (max - min).Length();

                tolerance = MathF.Max(span, 1e-6f) * Flat;
                cell = MathF.Max(span / MathF.Max(MathF.Cbrt(mesh.Faces.Length), 1f), 1e-6f);

                for (var f = 0; f < mesh.Faces.Length; f++)
                {
                    var (low, high) = Bounds(mesh.Faces[f].Select(corner => mesh.Points[corner]));
                    var (a, b) = (CellOf(low - new Vector3(tolerance)), CellOf(high + new Vector3(tolerance)));

                    if ((long)(b.X - a.X + 1) * (b.Y - a.Y + 1) * (b.Z - a.Z + 1) > MaxCellsPerFace)
                        continue;

                    for (var x = a.X; x <= b.X; x++)
                    {
                        for (var y = a.Y; y <= b.Y; y++)
                        {
                            for (var z = a.Z; z <= b.Z; z++)
                            {
                                if (!cells.TryGetValue((x, y, z), out var list))
                                    cells[(x, y, z)] = list = [];

                                list.Add(f);
                            }
                        }
                    }
                }
            }

            public int Host(Vector3 point, Vector3 normal)
            {
                var key = CellOf(point);

                if (!cells.TryGetValue((key.X, key.Y, key.Z), out var faces))
                    return -1;

                foreach (var face in faces)
                {
                    var faceNormal = mesh.NormalOf(face);

                    if (normal != Vector3.Zero && Vector3.Dot(faceNormal, normal) < SameSide)
                        continue;

                    var corners = mesh.Faces[face];

                    if (MathF.Abs(Vector3.Dot(point - mesh.Points[corners[0]], faceNormal)) > tolerance)
                        continue;

                    if (Inside(corners, faceNormal, point))
                        return face;
                }

                return -1;
            }

            private bool Inside(int[] corners, Vector3 normal, Vector3 point)
            {
                var (right, up) = Basis(normal);
                var (x, y) = (Vector3.Dot(point, right), Vector3.Dot(point, up));
                var inside = false;

                for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
                {
                    var (xi, yi) = (Vector3.Dot(mesh.Points[corners[i]], right), Vector3.Dot(mesh.Points[corners[i]], up));
                    var (xj, yj) = (Vector3.Dot(mesh.Points[corners[j]], right), Vector3.Dot(mesh.Points[corners[j]], up));

                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                        inside = !inside;
                }

                return inside;
            }

            private (int X, int Y, int Z) CellOf(Vector3 point) => ((int)MathF.Floor(point.X / cell), (int)MathF.Floor(point.Y / cell), (int)MathF.Floor(point.Z / cell));
        }
    }
}
