using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshAdd
    {
        private const float Radius = 0.5f;
        private const float TorusThickness = 0.25f;
        private const int MaxIcoLevels = 6;
        private const int MaxGrid = 200;

        public static (string Label, int Least, int Start) CountOf(MeshShape shape) => shape switch
        {
            MeshShape.Circle or MeshShape.UvSphere or MeshShape.Cylinder or MeshShape.Cone or MeshShape.Torus => ("分割", Primitives.MinSegments, 32),
            MeshShape.IcoSphere => ("細かさ", 1, 2),
            MeshShape.Grid => ("分割", 1, 10),
            _ => (string.Empty, 0, 0),
        };

        public static MeshTopology Make(MeshShape shape, int count)
        {
            var segments = Math.Clamp(count, Primitives.MinSegments, Primitives.MaxSegments);

            return shape switch
            {
                MeshShape.Cube => MeshTopology.FromGeometry(Primitives.Cube()),
                MeshShape.Circle => Circle(segments),
                MeshShape.UvSphere => MeshTopology.FromGeometry(Primitives.Sphere(segments)),
                MeshShape.IcoSphere => IcoSphere(Math.Clamp(count, 1, MaxIcoLevels)),
                MeshShape.Cylinder => MeshTopology.FromGeometry(Primitives.Cylinder(segments)),
                MeshShape.Cone => MeshTopology.FromGeometry(Primitives.Cone(segments)),
                MeshShape.Torus => MeshTopology.FromGeometry(Primitives.Torus(segments, TorusThickness)),
                MeshShape.Grid => Grid(Math.Clamp(count, 1, MaxGrid)),
                _ => Grid(1),
            };
        }

        public static (MeshTopology Mesh, int[] Added) Added(this MeshTopology mesh, MeshTopology shape, in Matrix4x4 place, bool keepsGroups = false)
        {
            var start = mesh.Points.Length;
            var joined = mesh.Join(shape, place, keepsGroups);

            return (joined, [.. Enumerable.Range(start, joined.Points.Length - start)]);
        }

        private static MeshTopology Circle(int segments)
        {
            var points = new Vector3[segments];

            for (var i = 0; i < segments; i++)
            {
                var angle = MathF.Tau * i / segments;

                points[i] = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * Radius;
            }

            var ring = Enumerable.Range(0, segments).Select(i => new MeshEdge(i, (i + 1) % segments));

            return new MeshTopology(points, [], [], [], 1, ring);
        }

        private static MeshTopology Grid(int cells)
        {
            var side = cells + 1;
            var points = new Vector3[side * side];
            var faces = new int[cells * cells][];

            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                    points[y * side + x] = new Vector3((float)x / cells - 0.5f, 0.5f - (float)y / cells, 0f);
            }

            for (var y = 0; y < cells; y++)
            {
                for (var x = 0; x < cells; x++)
                    faces[y * cells + x] = [y * side + x, y * side + x + 1, (y + 1) * side + x + 1, (y + 1) * side + x];
            }

            var plane = MeshTopology.FromGeometry(Primitives.Plane());
            var made = new MeshTopology(points, faces, new int[faces.Length], new bool[faces.Length], 1);

            return Vector3.Dot(made.NormalOf(0), plane.NormalOf(0)) < 0f ? Flipped(made) : made;
        }

        private static MeshTopology Flipped(MeshTopology mesh)
            => new(mesh.Points, [.. mesh.Faces.Select(face => face.Reverse().ToArray())], mesh.Groups, mesh.Smooth, mesh.GroupCount);

        private static MeshTopology IcoSphere(int levels)
        {
            var mesh = MeshTopology.FromGeometry(Primitives.Icosahedron());
            var points = mesh.Points.Select(point => Vector3.Normalize(point) * Radius).ToList();
            var faces = mesh.Faces.Select(face => face.ToArray()).ToList();

            for (var level = 0; level < levels; level++)
            {
                var middles = new Dictionary<(int, int), int>();

                int Middle(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);

                    if (middles.TryGetValue(key, out var found))
                        return found;

                    points.Add(Vector3.Normalize(points[a] + points[b]) * Radius);

                    return middles[key] = points.Count - 1;
                }

                var split = new List<int[]>(faces.Count * 4);

                foreach (var face in faces)
                {
                    var (a, b, c) = (face[0], face[1], face[2]);
                    var (ab, bc, ca) = (Middle(a, b), Middle(b, c), Middle(c, a));

                    split.Add([a, ab, ca]);
                    split.Add([ab, b, bc]);
                    split.Add([ca, bc, c]);
                    split.Add([ab, bc, ca]);
                }

                faces = split;
            }

            return new MeshTopology([.. points], [.. faces], new int[faces.Count], [.. faces.Select(_ => true)], 1);
        }
    }
}
