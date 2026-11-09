using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public enum OutlineForm
    {
        Extrude,

        Lathe,
    }

    public static class OutlineSolid
    {
        public const int MinPoints = 3;

        public const int MaxPoints = 64;

        private const float Same = 1e-5f;

        public static SurfaceGeometry Build(
            IReadOnlyList<Vector2> outline, OutlineForm form, float depth, int segments)
        {
            var points = Clean(outline);

            if (points.Count < MinPoints)
                return Primitives.Plane();

            return form is OutlineForm.Lathe
                ? Revolve(points, Math.Clamp(segments, Primitives.MinSegments, Primitives.MaxSegments))
                : Extrude(points, MathF.Max(Extent(points) * depth, 1e-3f));
        }

        public static int GroupCountOf(OutlineForm form) => form is OutlineForm.Lathe ? 1 : 3;

        public static float UnitScale(IReadOnlyList<Vector2> outline, OutlineForm form, float depth)
        {
            var points = Clean(outline);

            if (points.Count < MinPoints)
                return 1f;

            var reach = 0f;

            foreach (var point in points)
            {
                var across = form is OutlineForm.Lathe ? MathF.Max(point.X, 0f) : MathF.Abs(point.X);

                reach = MathF.Max(reach, MathF.Max(across, MathF.Abs(point.Y)));
            }

            if (form is OutlineForm.Extrude)
                reach = MathF.Max(reach, MathF.Max(Extent(points) * depth, 1e-3f) / 2f);

            return reach > 0f ? 0.5f / reach : 1f;
        }

        public static List<Vector2> Clean(IReadOnlyList<Vector2> outline)
        {
            var points = new List<Vector2>();

            foreach (var point in outline)
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                    continue;

                if (points.Count > 0 && Vector2.Distance(points[^1], point) < Same)
                    continue;

                points.Add(point);
            }

            while (points.Count > 1 && Vector2.Distance(points[0], points[^1]) < Same)
                points.RemoveAt(points.Count - 1);

            if (points.Count >= MinPoints && SignedArea(points) < 0f)
                points.Reverse();

            return points;
        }

        public static float Extent(IReadOnlyList<Vector2> points)
        {
            if (points.Count == 0)
                return 1f;

            var min = points[0];
            var max = points[0];

            foreach (var point in points)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            var span = max - min;

            return MathF.Max(MathF.Max(span.X, span.Y), 1e-3f);
        }

        public static float SignedArea(IReadOnlyList<Vector2> points)
        {
            var sum = 0f;

            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];

                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum / 2f;
        }

        public static List<(int A, int B, int C)> Triangulate(IReadOnlyList<Vector2> points)
        {
            var found = new List<(int, int, int)>();
            var left = new List<int>(points.Count);

            for (var i = 0; i < points.Count; i++)
                left.Add(i);

            var guard = points.Count * points.Count + 8;

            while (left.Count > 3 && guard-- > 0)
            {
                var clipped = false;

                for (var i = 0; i < left.Count; i++)
                {
                    var a = left[(i + left.Count - 1) % left.Count];
                    var b = left[i];
                    var c = left[(i + 1) % left.Count];

                    if (!IsEar(points, left, a, b, c))
                        continue;

                    found.Add((a, b, c));
                    left.RemoveAt(i);
                    clipped = true;

                    break;
                }

                if (!clipped)
                    break;
            }

            for (var i = 1; i + 1 < left.Count; i++)
                found.Add((left[0], left[i], left[i + 1]));

            return found;
        }

        private static bool IsEar(IReadOnlyList<Vector2> points, List<int> left, int a, int b, int c)
        {
            var first = points[a];
            var middle = points[b];
            var last = points[c];

            if (Cross(middle - first, last - middle) <= 1e-9f)
                return false;

            foreach (var index in left)
            {
                if (index == a || index == b || index == c)
                    continue;

                if (Covers(points[index], first, middle, last))
                    return false;
            }

            return true;
        }

        private static bool Covers(in Vector2 point, in Vector2 a, in Vector2 b, in Vector2 c)
        {
            var first = Cross(b - a, point - a);
            var second = Cross(c - b, point - b);
            var third = Cross(a - c, point - c);

            return first >= 0f && second >= 0f && third >= 0f;
        }

        private static float Cross(in Vector2 a, in Vector2 b) => a.X * b.Y - a.Y * b.X;

        private static SurfaceGeometry Extrude(List<Vector2> points, float depth)
        {
            var count = points.Count;
            var half = depth / 2f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var faces = new List<SurfaceFace>();

            for (var i = 0; i < count; i++)
            {
                var from = points[i];
                var to = points[(i + 1) % count];
                var normal = new Vector3(EdgeNormal(from, to), 0f);

                faces.Add(new SurfaceFace(
                    [
                        Add(vertices, normals, new Vector3(from, half), normal),
                        Add(vertices, normals, new Vector3(to, half), normal),
                        Add(vertices, normals, new Vector3(to, -half), normal),
                        Add(vertices, normals, new Vector3(from, -half), normal),
                    ],
                    0,
                    false));
            }

            var front = new Vector3(0f, 0f, 1f);
            var back = new Vector3(0f, 0f, -1f);

            foreach (var (a, b, c) in Triangulate(points))
            {
                faces.Add(new SurfaceFace(
                    [
                        Add(vertices, normals, new Vector3(points[a], half), front),
                        Add(vertices, normals, new Vector3(points[b], half), front),
                        Add(vertices, normals, new Vector3(points[c], half), front),
                    ],
                    1,
                    false));

                faces.Add(new SurfaceFace(
                    [
                        Add(vertices, normals, new Vector3(points[c], -half), back),
                        Add(vertices, normals, new Vector3(points[b], -half), back),
                        Add(vertices, normals, new Vector3(points[a], -half), back),
                    ],
                    2,
                    false));
            }

            return new SurfaceGeometry([.. vertices], [.. normals], [.. faces], 3)
                .FacingOutward()
                .ScaledToUnit();
        }

        private static SurfaceGeometry Revolve(List<Vector2> profile, int segments)
        {
            var count = profile.Count;
            var flats = new Vector2[count];

            for (var i = 0; i < count; i++)
            {
                var before = profile[(i + count - 1) % count];
                var here = profile[i];
                var after = profile[(i + 1) % count];

                flats[i] = Blend(EdgeNormal(before, here), EdgeNormal(here, after));
            }

            var vertices = new Vector3[count * segments];
            var normals = new Vector3[count * segments];

            for (var step = 0; step < segments; step++)
            {
                var angle = MathF.Tau * step / segments;
                var cos = MathF.Cos(angle);
                var sin = MathF.Sin(angle);

                for (var i = 0; i < count; i++)
                {
                    var radius = MathF.Max(profile[i].X, 0f);

                    vertices[step * count + i] = new Vector3(radius * cos, profile[i].Y, radius * sin);
                    normals[step * count + i] = new Vector3(flats[i].X * cos, flats[i].Y, flats[i].X * sin);
                }
            }

            var faces = new List<SurfaceFace>();

            int At(int step, int index) => step % segments * count + index % count;

            for (var step = 0; step < segments; step++)
            {
                for (var i = 0; i < count; i++)
                {
                    faces.Add(new SurfaceFace(
                        [At(step, i), At(step, i + 1), At(step + 1, i + 1), At(step + 1, i)],
                        0,
                        true));
                }
            }

            return new SurfaceGeometry(vertices, normals, [.. faces], 1)
                .FacingOutward()
                .ScaledToUnit();
        }

        private static Vector2 EdgeNormal(in Vector2 from, in Vector2 to)
        {
            var edge = to - from;

            return edge.LengthSquared() > 1e-12f
                ? Vector2.Normalize(new Vector2(edge.Y, -edge.X))
                : Vector2.UnitX;
        }

        private static Vector2 Blend(in Vector2 first, in Vector2 second)
        {
            var sum = first + second;

            return sum.LengthSquared() > 1e-12f ? Vector2.Normalize(sum) : first;
        }

        private static int Add(List<Vector3> vertices, List<Vector3> normals, in Vector3 point, in Vector3 normal)
        {
            vertices.Add(point);
            normals.Add(normal);

            return vertices.Count - 1;
        }
    }
}
