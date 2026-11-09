using System.Numerics;

namespace Noise3D
{
    public enum NoiseVolumeKind
    {
        Value,
        Perlin,
        Cellular,
        Voronoi,
        Simplex,
    }

    public static class NoiseVolume
    {
        public const int Cells = 16;

        public const int TexelsPerCell = 8;

        public const int Size = Cells * TexelsPerCell;

        private const int MaxCached = 4;

        private static readonly Lock gate = new();

        private static readonly List<((NoiseVolumeKind Kind, int Seed) Key, ushort[] Data)> cache = [];

        public static NoiseVolumeKind? KindOf(Noise3DType type) => type switch
        {
            Noise3DType.Value or Noise3DType.Marble => NoiseVolumeKind.Value,
            Noise3DType.Perlin => NoiseVolumeKind.Perlin,
            Noise3DType.Cellular => NoiseVolumeKind.Cellular,
            Noise3DType.Voronoi => NoiseVolumeKind.Voronoi,
            Noise3DType.Simplex => NoiseVolumeKind.Simplex,
            _ => null,
        };

        public static bool IsPeriodic(NoiseVolumeKind kind) => kind != NoiseVolumeKind.Simplex;

        public static ushort[] Get(NoiseVolumeKind kind, int seed)
        {
            var key = (kind, seed);

            lock (gate)
            {
                var index = cache.FindIndex(entry => entry.Key == key);

                if (index >= 0)
                {
                    var hit = cache[index];
                    cache.RemoveAt(index);
                    cache.Add(hit);
                    return hit.Data;
                }
            }

            var data = Bake(kind, (uint)seed);

            lock (gate)
            {
                cache.RemoveAll(entry => entry.Key == key);
                cache.Add((key, data));

                while (cache.Count > MaxCached)
                    cache.RemoveAt(0);
            }

            return data;
        }

        public static ushort[] Bake(NoiseVolumeKind kind, uint seed)
        {
            var data = new ushort[Size * Size * Size];

            Parallel.For(0, Size, z =>
            {
                for (var y = 0; y < Size; y++)
                {
                    for (var x = 0; x < Size; x++)
                    {
                        var p = (new Vector3(x, y, z) + new Vector3(0.5f)) / TexelsPerCell;
                        var signed = Sample(kind, p, seed);
                        var unit = Math.Clamp(signed * 0.5f + 0.5f, 0f, 1f);

                        data[(z * Size + y) * Size + x] = (ushort)MathF.Round(unit * ushort.MaxValue);
                    }
                }
            });

            return data;
        }

        public static float Sample(NoiseVolumeKind kind, Vector3 p, uint seed) => kind switch
        {
            NoiseVolumeKind.Perlin => Perlin(p, seed),
            NoiseVolumeKind.Cellular => Math.Clamp(NearestCell(p, seed, out _), 0f, 1f) * 2f - 1f,
            NoiseVolumeKind.Voronoi => NearestCellValue(p, seed) * 2f - 1f,
            NoiseVolumeKind.Simplex => Simplex(p, seed),
            _ => Value(p, seed) * 2f - 1f,
        };

        private static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return x;
            }
        }

        private static int Wrap(int value) => ((value % Cells) + Cells) % Cells;

        private static uint HashCell(int x, int y, int z, uint seed, bool periodic)
        {
            if (periodic)
            {
                x = Wrap(x);
                y = Wrap(y);
                z = Wrap(z);
            }

            unchecked
            {
                return Hash((uint)x + Hash((uint)y + Hash((uint)z + Hash(seed))));
            }
        }

        private static float Random01(int x, int y, int z, uint seed, bool periodic = true)
            => (HashCell(x, y, z, seed, periodic) >> 8) / 16777215f;

        private static Vector3 RandomVector01(int x, int y, int z, uint seed, bool periodic = true)
        {
            var h = HashCell(x, y, z, seed, periodic);

            unchecked
            {
                return new Vector3(Hash(h) >> 8, Hash(h + 1U) >> 8, Hash(h + 2U) >> 8) / 16777215f;
            }
        }

        private static Vector3 Gradient(int x, int y, int z, uint seed, bool periodic = true)
        {
            var vector = RandomVector01(x, y, z, seed, periodic) * 2f - Vector3.One;

            return vector.LengthSquared() > 1e-8f ? Vector3.Normalize(vector) : Vector3.UnitX;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Value(Vector3 p, uint seed)
        {
            var (x, y, z) = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
            var (u, v, w) = (Fade(p.X - x), Fade(p.Y - y), Fade(p.Z - z));

            return float.Lerp(
                float.Lerp(
                    float.Lerp(Random01(x, y, z, seed), Random01(x + 1, y, z, seed), u),
                    float.Lerp(Random01(x, y + 1, z, seed), Random01(x + 1, y + 1, z, seed), u), v),
                float.Lerp(
                    float.Lerp(Random01(x, y, z + 1, seed), Random01(x + 1, y, z + 1, seed), u),
                    float.Lerp(Random01(x, y + 1, z + 1, seed), Random01(x + 1, y + 1, z + 1, seed), u), v),
                w);
        }

        private static float Perlin(Vector3 p, uint seed)
        {
            var (x, y, z) = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
            var f = p - new Vector3(x, y, z);
            var (u, v, w) = (Fade(f.X), Fade(f.Y), Fade(f.Z));

            float Corner(int dx, int dy, int dz)
                => Vector3.Dot(Gradient(x + dx, y + dy, z + dz, seed), f - new Vector3(dx, dy, dz));

            var n = float.Lerp(
                float.Lerp(float.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), u), float.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), u), v),
                float.Lerp(float.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), u), float.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), u), v),
                w);

            return Math.Clamp(n * 1.5f, -1f, 1f);
        }

        private static float NearestCell(Vector3 p, uint seed, out (int X, int Y, int Z) nearest)
        {
            var (bx, by, bz) = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
            var distance = float.MaxValue;
            nearest = (bx, by, bz);

            for (var z = bz - 1; z <= bz + 1; z++)
            {
                for (var y = by - 1; y <= by + 1; y++)
                {
                    for (var x = bx - 1; x <= bx + 1; x++)
                    {
                        var feature = new Vector3(x, y, z) + RandomVector01(x, y, z, seed);
                        var d = Vector3.DistanceSquared(feature, p);

                        if (d < distance)
                        {
                            distance = d;
                            nearest = (x, y, z);
                        }
                    }
                }
            }

            return MathF.Sqrt(distance);
        }

        private static float NearestCellValue(Vector3 p, uint seed)
        {
            NearestCell(p, seed, out var nearest);

            unchecked
            {
                return Random01(nearest.X, nearest.Y, nearest.Z, seed + 101U);
            }
        }

        private static float Simplex(Vector3 v, uint seed)
        {
            const float F3 = 1f / 3f;
            const float G3 = 1f / 6f;

            var s = (v.X + v.Y + v.Z) * F3;
            var i = new Vector3(MathF.Floor(v.X + s), MathF.Floor(v.Y + s), MathF.Floor(v.Z + s));
            var t = (i.X + i.Y + i.Z) * G3;
            var x0 = v - i + new Vector3(t);

            Vector3 i1, i2;

            if (x0.X >= x0.Y)
            {
                if (x0.Y >= x0.Z) { i1 = Vector3.UnitX; i2 = new Vector3(1, 1, 0); }
                else if (x0.X >= x0.Z) { i1 = Vector3.UnitX; i2 = new Vector3(1, 0, 1); }
                else { i1 = Vector3.UnitZ; i2 = new Vector3(1, 0, 1); }
            }
            else
            {
                if (x0.Y < x0.Z) { i1 = Vector3.UnitZ; i2 = new Vector3(0, 1, 1); }
                else if (x0.X < x0.Z) { i1 = Vector3.UnitY; i2 = new Vector3(0, 1, 1); }
                else { i1 = Vector3.UnitY; i2 = new Vector3(1, 1, 0); }
            }

            float Corner(Vector3 offset, Vector3 cell)
            {
                var weight = 0.6f - offset.LengthSquared();

                if (weight <= 0f)
                    return 0f;

                weight *= weight;
                return weight * weight * Vector3.Dot(Gradient((int)cell.X, (int)cell.Y, (int)cell.Z, seed, periodic: false), offset);
            }

            var n = Corner(x0, i)
                  + Corner(x0 - i1 + new Vector3(G3), i + i1)
                  + Corner(x0 - i2 + new Vector3(2f * G3), i + i2)
                  + Corner(x0 - Vector3.One + new Vector3(3f * G3), i + Vector3.One);

            return Math.Clamp(32f * n, -1f, 1f);
        }
    }
}
