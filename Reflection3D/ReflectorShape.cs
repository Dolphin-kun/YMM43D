using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace Reflection3D
{
    internal sealed class ReflectorShape
    {
        public const int MaxPlanes = 64;
        private const float PlanarShare = 0.04f;
        private const float SameDirection = 0.9995f;
        private const float SameOffset = 1e-3f;

        private ReflectorShape(ReflectorVertex[] vertices, ReflectorPlane[] planes, bool hasCurved, bool isOpen, Vector3 center)
        {
            Vertices = vertices;
            Planes = planes;
            HasCurved = hasCurved;
            IsOpen = isOpen;
            Center = center;
        }

        public ReflectorVertex[] Vertices { get; }

        public ReflectorPlane[] Planes { get; }

        public bool HasCurved { get; }

        public bool IsOpen { get; }

        public Vector3 Center { get; }

        public static ReflectorShape Build(SurfaceGeometry geometry)
        {
            var points = geometry.Vertices;
            var faces = geometry.Faces;
            var extent = Extent(points, out var center);
            var tolerance = SameOffset * MathF.Max(extent, 1e-6f);

            var clusters = new List<(Vector3 Normal, float Offset, Vector3 Weighted, float Area)>();
            var faceCluster = new int[faces.Length];
            var faceNormal = new Vector3[faces.Length];
            var total = 0f;

            for (var f = 0; f < faces.Length; f++)
            {
                var indices = faces[f].Indices;
                var (normal, area, centroid) = Measure(points, indices);

                faceNormal[f] = normal;
                faceCluster[f] = -1;
                total += area;

                if (area <= 0f)
                    continue;

                var offset = Vector3.Dot(normal, points[indices[0]]);
                var found = clusters.FindIndex(c => Vector3.Dot(c.Normal, normal) > SameDirection && MathF.Abs(c.Offset - offset) < tolerance);

                if (found < 0)
                {
                    clusters.Add((normal, offset, centroid * area, area));
                    found = clusters.Count - 1;
                }
                else
                {
                    var (sameNormal, sameOffset, weighted, covered) = clusters[found];
                    clusters[found] = (sameNormal, sameOffset, weighted + centroid * area, covered + area);
                }

                faceCluster[f] = found;
            }

            var planar = Enumerable.Range(0, clusters.Count)
                .Where(i => clusters[i].Area >= total * PlanarShare)
                .OrderByDescending(i => clusters[i].Area)
                .Take(MaxPlanes)
                .ToArray();

            var planeOf = new Dictionary<int, int>();
            var planes = new ReflectorPlane[planar.Length];

            for (var i = 0; i < planar.Length; i++)
            {
                var (normal, _, weighted, area) = clusters[planar[i]];

                planeOf[planar[i]] = i;
                planes[i] = new ReflectorPlane(normal, weighted / area, area);
            }

            var vertices = new List<ReflectorVertex>();
            var hasCurved = false;

            for (var f = 0; f < faces.Length; f++)
            {
                var face = faces[f];
                var plane = faceCluster[f] >= 0 && planeOf.TryGetValue(faceCluster[f], out var p) ? p : -1;

                if (plane < 0)
                    hasCurved = true;

                for (var i = 1; i + 1 < face.Indices.Length; i++)
                {
                    foreach (var corner in (ReadOnlySpan<int>)[face.Indices[0], face.Indices[i], face.Indices[i + 1]])
                    {
                        var normal = plane < 0 && face.IsSmooth ? Smooth(geometry, corner, faceNormal[f]) : faceNormal[f];

                        vertices.Add(new ReflectorVertex(points[corner], normal, plane));
                    }
                }
            }

            return new ReflectorShape([.. vertices], planes, hasCurved, MeshDistanceField.IsOpen(geometry), center);
        }

        private static (Vector3 Normal, float Area, Vector3 Centroid) Measure(Vector3[] points, int[] indices)
        {
            var sum = Vector3.Zero;
            var centroid = Vector3.Zero;

            for (var i = 1; i + 1 < indices.Length; i++)
                sum += Vector3.Cross(points[indices[i]] - points[indices[0]], points[indices[i + 1]] - points[indices[0]]);

            foreach (var index in indices)
                centroid += points[index];

            var length = sum.Length();

            return length > 1e-12f ? (-sum / length, length / 2f, centroid / indices.Length) : (Vector3.Zero, 0f, centroid / indices.Length);
        }

        private static Vector3 Smooth(SurfaceGeometry geometry, int corner, in Vector3 flat)
        {
            var found = corner < geometry.Normals.Length ? geometry.Normals[corner] : Vector3.Zero;

            return found.LengthSquared() > 1e-10f ? Vector3.Normalize(found) : flat;
        }

        private static float Extent(Vector3[] points, out Vector3 center)
        {
            if (points.Length == 0)
            {
                center = Vector3.Zero;
                return 0f;
            }

            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);

            foreach (var point in points)
            {
                low = Vector3.Min(low, point);
                high = Vector3.Max(high, point);
            }

            center = (low + high) / 2f;

            var size = high - low;

            return MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        }
    }
}
