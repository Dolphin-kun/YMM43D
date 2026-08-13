using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class Primitives
    {
        public const int MinSegments = 3;

        public const int MaxSegments = 128;

        public static SurfaceGeometry Plane() => new SurfaceGeometry(
            [
                new(-0.5f, 0.5f, 0f), new(0.5f, 0.5f, 0f),
                new(0.5f, -0.5f, 0f), new(-0.5f, -0.5f, 0f),
            ],
            [
                new(0f, 0f, 1f), new(0f, 0f, 1f), new(0f, 0f, 1f), new(0f, 0f, 1f),
            ],
            [new SurfaceFace([0, 1, 2, 3], 0, false)],
            1).FacingOutward();

        public static SurfaceGeometry Tetrahedron() => SurfaceGeometry.Faceted(
            [
                new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1),
            ],
            [
                [0, 1, 2], [0, 3, 1], [0, 2, 3], [1, 3, 2],
            ]).ScaledToUnit();

        public static SurfaceGeometry Cube() => SurfaceGeometry.Faceted(
            [
                new(-1, 1, 1), new(1, 1, 1), new(1, -1, 1), new(-1, -1, 1),
                new(-1, 1, -1), new(1, 1, -1), new(1, -1, -1), new(-1, -1, -1),
            ],
            [
                [0, 1, 2, 3],
                [5, 4, 7, 6],
                [4, 0, 3, 7],
                [1, 5, 6, 2],
                [4, 5, 1, 0],
                [3, 2, 6, 7],
            ]).ScaledToUnit();

        public static SurfaceGeometry Octahedron() => SurfaceGeometry.Faceted(
            [
                new(1, 0, 0), new(-1, 0, 0),
                new(0, 1, 0), new(0, -1, 0),
                new(0, 0, 1), new(0, 0, -1),
            ],
            [
                [0, 2, 4], [2, 1, 4], [1, 3, 4], [3, 0, 4],
                [2, 0, 5], [1, 2, 5], [3, 1, 5], [0, 3, 5],
            ]).ScaledToUnit();

        public static SurfaceGeometry Icosahedron()
        {
            var phi = (1f + MathF.Sqrt(5f)) / 2f;

            return SurfaceGeometry.Faceted(
                [
                    new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0),
                    new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi),
                    new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1),
                ],
                [
                    [0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11],
                    [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8],
                    [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9],
                    [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1],
                ]).ScaledToUnit();
        }

        public static SurfaceGeometry Dodecahedron()
        {
            var phi = (1f + MathF.Sqrt(5f)) / 2f;
            var inv = 1f / phi;

            return SurfaceGeometry.Faceted(
                [
                    new(1, 1, 1), new(1, 1, -1), new(1, -1, 1), new(1, -1, -1),
                    new(-1, 1, 1), new(-1, 1, -1), new(-1, -1, 1), new(-1, -1, -1),
                    new(0, inv, phi), new(0, inv, -phi), new(0, -inv, phi), new(0, -inv, -phi),
                    new(inv, phi, 0), new(inv, -phi, 0), new(-inv, phi, 0), new(-inv, -phi, 0),
                    new(phi, 0, inv), new(phi, 0, -inv), new(-phi, 0, inv), new(-phi, 0, -inv),
                ],
                [
                    [0, 8, 10, 2, 16],
                    [0, 16, 17, 1, 12],
                    [0, 12, 14, 4, 8],
                    [8, 4, 18, 6, 10],
                    [10, 6, 15, 13, 2],
                    [2, 13, 3, 17, 16],
                    [1, 17, 3, 11, 9],
                    [1, 9, 5, 14, 12],
                    [14, 5, 19, 18, 4],
                    [18, 19, 7, 15, 6],
                    [15, 7, 11, 3, 13],
                    [9, 11, 7, 19, 5],
                ]).ScaledToUnit();
        }

        public static SurfaceGeometry Sphere(int segments)
        {
            var around = Clamp(segments);
            var stacks = Math.Max(2, around / 2);

            var vertices = new List<Vector3> { new(0f, 0.5f, 0f) };
            var faces = new List<SurfaceFace>();

            for (var stack = 1; stack < stacks; stack++)
            {
                var polar = MathF.PI * stack / stacks;
                var height = 0.5f * MathF.Cos(polar);
                var radius = 0.5f * MathF.Sin(polar);

                for (var step = 0; step < around; step++)
                {
                    var angle = MathF.Tau * step / around;
                    vertices.Add(new Vector3(radius * MathF.Cos(angle), height, radius * MathF.Sin(angle)));
                }
            }

            var bottom = vertices.Count;
            vertices.Add(new Vector3(0f, -0.5f, 0f));

            int Ring(int stack, int step) => 1 + (stack - 1) * around + step % around;

            for (var step = 0; step < around; step++)
            {
                faces.Add(new SurfaceFace([0, Ring(1, step), Ring(1, step + 1)], 0, true));
                faces.Add(new SurfaceFace([bottom, Ring(stacks - 1, step), Ring(stacks - 1, step + 1)], 0, true));
            }

            for (var stack = 1; stack < stacks - 1; stack++)
            {
                for (var step = 0; step < around; step++)
                {
                    faces.Add(new SurfaceFace(
                        [Ring(stack, step), Ring(stack, step + 1), Ring(stack + 1, step + 1), Ring(stack + 1, step)],
                        0,
                        true));
                }
            }

            return Radial([.. vertices], [.. faces], 1);
        }

        public static SurfaceGeometry Cylinder(int segments)
        {
            var around = Clamp(segments);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var faces = new List<SurfaceFace>();

            for (var step = 0; step < around; step++)
            {
                var direction = Around(step, around);

                vertices.Add(direction * 0.5f + new Vector3(0f, 0.5f, 0f));
                normals.Add(direction);
                vertices.Add(direction * 0.5f + new Vector3(0f, -0.5f, 0f));
                normals.Add(direction);
            }

            for (var step = 0; step < around; step++)
            {
                var next = (step + 1) % around;

                faces.Add(new SurfaceFace([step * 2, next * 2, next * 2 + 1, step * 2 + 1], 0, true));
            }

            AddCap(vertices, normals, faces, around, 0.5f, 1);
            AddCap(vertices, normals, faces, around, -0.5f, 2);

            return new SurfaceGeometry([.. vertices], [.. normals], [.. faces], 3).FacingOutward();
        }

        public static SurfaceGeometry Cone(int segments)
        {
            var around = Clamp(segments);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var faces = new List<SurfaceFace>();

            var slope = Vector3.Normalize(new Vector3(1f, 0.5f, 0f));

            for (var step = 0; step < around; step++)
            {
                var direction = Around(step, around);

                vertices.Add(direction * 0.5f + new Vector3(0f, -0.5f, 0f));
                normals.Add(direction * slope.X + new Vector3(0f, slope.Y, 0f));
            }

            for (var step = 0; step < around; step++)
            {
                var next = (step + 1) % around;
                var middle = Vector3.Normalize(Around(step, around) + Around(next, around));

                vertices.Add(new Vector3(0f, 0.5f, 0f));
                normals.Add(middle * slope.X + new Vector3(0f, slope.Y, 0f));

                faces.Add(new SurfaceFace([vertices.Count - 1, step, next], 0, true));
            }

            AddCap(vertices, normals, faces, around, -0.5f, 1);

            return new SurfaceGeometry([.. vertices], [.. normals], [.. faces], 2).FacingOutward();
        }

        public static SurfaceGeometry Torus(int segments, float thickness)
        {
            var around = Clamp(segments);
            var tube = Math.Max(3, around / 2);

            var ratio = Math.Clamp(thickness, 0.01f, 1f);
            var ringRadius = 0.5f / (1f + ratio);
            var tubeRadius = 0.5f - ringRadius;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var faces = new List<SurfaceFace>();

            for (var step = 0; step < around; step++)
            {
                var direction = Around(step, around);

                for (var turn = 0; turn < tube; turn++)
                {
                    var angle = MathF.Tau * turn / tube;
                    var normal = direction * MathF.Cos(angle) + new Vector3(0f, MathF.Sin(angle), 0f);

                    vertices.Add(direction * ringRadius + normal * tubeRadius);
                    normals.Add(normal);
                }
            }

            int At(int step, int turn) => step % around * tube + turn % tube;

            for (var step = 0; step < around; step++)
            {
                for (var turn = 0; turn < tube; turn++)
                {
                    faces.Add(new SurfaceFace(
                        [At(step, turn), At(step + 1, turn), At(step + 1, turn + 1), At(step, turn + 1)],
                        0,
                        true));
                }
            }

            return new SurfaceGeometry([.. vertices], [.. normals], [.. faces], 1).FacingOutward();
        }

        private static void AddCap(
            List<Vector3> vertices, List<Vector3> normals, List<SurfaceFace> faces,
            int around, float height, int group)
        {
            var normal = new Vector3(0f, MathF.Sign(height), 0f);
            var first = vertices.Count;
            var rim = new int[around];

            for (var step = 0; step < around; step++)
            {
                vertices.Add(Around(step, around) * 0.5f + new Vector3(0f, height, 0f));
                normals.Add(normal);
                rim[step] = first + step;
            }

            faces.Add(new SurfaceFace(rim, group, false));
        }

        private static SurfaceGeometry Radial(Vector3[] vertices, SurfaceFace[] faces, int groups)
        {
            var normals = new Vector3[vertices.Length];

            for (var i = 0; i < vertices.Length; i++)
            {
                normals[i] = vertices[i].LengthSquared() > 1e-12f
                    ? Vector3.Normalize(vertices[i])
                    : Vector3.UnitY;
            }

            return new SurfaceGeometry(vertices, normals, faces, groups).FacingOutward();
        }

        private static Vector3 Around(int step, int count)
        {
            var angle = MathF.Tau * step / count;

            return new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
        }

        private static int Clamp(int segments) => Math.Clamp(segments, MinSegments, MaxSegments);
    }
}
