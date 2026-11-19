using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace Bezier3D
{
    public static class BezierTube
    {
        public const int LeastSides = 3;
        public const int MostSides = 64;
        public const int GroupSteps = 64;

        private const int RoundRings = 3;

        public static SurfaceGeometry Build(
            IReadOnlyList<PathPoint[]> runs,
            float radius,
            float taper,
            int sides,
            bool flat,
            BezierCap start,
            BezierCap end,
            BezierCap dash)
        {
            var around = flat ? 2 : Math.Clamp(sides, LeastSides, MostSides);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var faces = new List<SurfaceFace>();

            for (var run = 0; run < runs.Count; run++)
            {
                var points = runs[run];

                if (points.Length < 2)
                    continue;

                var first = run == 0 ? start : dash;
                var last = run == runs.Count - 1 ? end : dash;

                Add(points, radius, taper, around, flat, first, last, vertices, normals, faces);
            }

            return new SurfaceGeometry([.. vertices], [.. normals], [.. faces], GroupSteps);
        }

        private static void Add(
            PathPoint[] points,
            float radius,
            float taper,
            int around,
            bool flat,
            BezierCap start,
            BezierCap end,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<SurfaceFace> faces)
        {
            var carried = Vector3.UnitY;
            var rings = new List<int>();
            var middles = new List<Vector3>();
            var aims = new List<Vector3>();
            var widths = new List<float>();

            for (var i = 0; i < points.Length; i++)
            {
                var center = points[i].Position;
                var aim = Aim(points, i);

                var across = Vector3.Cross(carried, aim);

                if (across.LengthSquared() < 1e-10f)
                    across = Vector3.Cross(Vector3.UnitX, aim);

                if (across.LengthSquared() < 1e-10f)
                    across = Vector3.Cross(Vector3.UnitZ, aim);

                across = Vector3.Normalize(across);

                var up = Vector3.Cross(aim, across);

                up = up.LengthSquared() > 1e-10f ? Vector3.Normalize(up) : Vector3.UnitY;
                carried = up;

                var width = MathF.Max(radius * (1f - taper * points[i].At), 0f);

                rings.Add(vertices.Count);
                middles.Add(center);
                aims.Add(aim);
                widths.Add(width);

                for (var side = 0; side < around; side++)
                {
                    if (flat)
                    {
                        vertices.Add(center + across * (side == 0 ? -width : width));
                        normals.Add(up);
                        continue;
                    }

                    var angle = MathF.Tau * side / around;
                    var outward = across * MathF.Cos(angle) + up * MathF.Sin(angle);

                    vertices.Add(center + outward * width);
                    normals.Add(outward);
                }
            }

            var wraps = flat ? 1 : around;

            for (var i = 0; i + 1 < rings.Count; i++)
            {
                var group = Group(points[i].At);

                for (var side = 0; side < wraps; side++)
                {
                    var next = (side + 1) % around;

                    faces.Add(new SurfaceFace(
                        [
                            rings[i] + side,
                            rings[i] + next,
                            rings[i + 1] + next,
                            rings[i + 1] + side,
                        ],
                        group,
                        true));
                }
            }

            if (flat || around < 3)
                return;

            Cap(start, middles[0], -aims[0], widths[0], rings[0], around, Group(points[0].At),
                vertices, normals, faces, opening: true);

            Cap(end, middles[^1], aims[^1], widths[^1], rings[^1], around, Group(points[^1].At),
                vertices, normals, faces, opening: false);
        }

        private static void Cap(
            BezierCap kind,
            in Vector3 center,
            in Vector3 outward,
            float width,
            int ring,
            int around,
            int group,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<SurfaceFace> faces,
            bool opening)
        {
            if (width <= 0f)
                return;

            if (kind is BezierCap.Flat or BezierCap.Square)
            {
                var rim = ring;

                if (kind == BezierCap.Square)
                {
                    rim = vertices.Count;

                    for (var side = 0; side < around; side++)
                    {
                        vertices.Add(vertices[ring + side] + outward * width);
                        normals.Add(normals[ring + side]);
                    }

                    for (var side = 0; side < around; side++)
                    {
                        var next = (side + 1) % around;

                        faces.Add(new SurfaceFace(
                            opening
                                ? [ring + side, rim + side, rim + next, ring + next]
                                : [ring + side, ring + next, rim + next, rim + side],
                            group,
                            true));
                    }
                }

                var corners = new int[around];

                for (var side = 0; side < around; side++)
                    corners[side] = rim + (opening ? around - 1 - side : side);

                faces.Add(new SurfaceFace(corners, group, false));
                return;
            }

            if (kind == BezierCap.Point)
            {
                var tip = vertices.Count;

                vertices.Add(center + outward * width);
                normals.Add(outward);

                for (var side = 0; side < around; side++)
                {
                    var next = (side + 1) % around;

                    faces.Add(new SurfaceFace(
                        opening
                            ? [tip, ring + next, ring + side]
                            : [tip, ring + side, ring + next],
                        group,
                        true));
                }

                return;
            }

            var previous = ring;

            for (var step = 1; step <= RoundRings; step++)
            {
                var angle = MathF.PI / 2f * step / RoundRings;
                var lift = MathF.Sin(angle) * width;
                var shrink = MathF.Cos(angle);
                var made = vertices.Count;

                for (var side = 0; side < around; side++)
                {
                    var rim = vertices[ring + side] - center;
                    var normal = Vector3.Normalize(rim * shrink + outward * MathF.Sin(angle) * width);

                    vertices.Add(center + rim * shrink + outward * lift);
                    normals.Add(normal.LengthSquared() > 1e-10f ? normal : outward);
                }

                for (var side = 0; side < around; side++)
                {
                    var next = (side + 1) % around;

                    faces.Add(new SurfaceFace(
                        opening
                            ? [previous + side, made + side, made + next, previous + next]
                            : [previous + side, previous + next, made + next, made + side],
                        group,
                        true));
                }

                previous = made;
            }
        }

        private static Vector3 Aim(PathPoint[] points, int index)
        {
            var from = points[Math.Max(index - 1, 0)].Position;
            var to = points[Math.Min(index + 1, points.Length - 1)].Position;
            var along = to - from;

            if (along.LengthSquared() > 1e-12f)
                return Vector3.Normalize(along);

            var fallback = points[^1].Position - points[0].Position;

            return fallback.LengthSquared() > 1e-12f ? Vector3.Normalize(fallback) : Vector3.UnitX;
        }

        private static int Group(float at) => Math.Clamp((int)MathF.Round(Math.Clamp(at, 0f, 1f) * (GroupSteps - 1)), 0, GroupSteps - 1);
    }
}
