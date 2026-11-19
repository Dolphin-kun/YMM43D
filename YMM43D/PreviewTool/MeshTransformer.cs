using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal sealed class MeshTransformer
    {
        private readonly MeshSelection selection;
        private readonly Matrix4x4 toLocal;
        private readonly Vector3[] placed;
        private readonly Dictionary<int, Vector3> origins;
        private readonly bool individual;

        private Dictionary<int, float> weights = [];

        public MeshTransformer(MeshTopology mesh, in Matrix4x4 toWorld, MeshSelection selection, PivotPoint pivot, in Vector3 cursor, ProportionalEdit? proportional)
        {
            Mesh = mesh;
            this.selection = selection;

            Matrix4x4.Invert(toWorld, out toLocal);

            placed = new Vector3[Mesh.Points.Length];

            for (var i = 0; i < placed.Length; i++)
                placed[i] = Vector3.Transform(Mesh.Points[i], toWorld);

            Pivot = MeshPivot.Compute(mesh, toWorld, selection, pivot, cursor) ?? Vector3.Transform(Vector3.Zero, toWorld);
            individual = pivot is PivotPoint.Individual;
            origins = individual ? MeshPivot.Islands(mesh, toWorld, selection) : [];
            Proportional = proportional;
        }

        public Vector3 Pivot { get; }

        public MeshTopology Mesh { get; }

        public IReadOnlyList<Vector3> Placed => placed;

        public ProportionalEdit? Proportional
        {
            get;
            set
            {
                field = value;
                weights = value is { } shaped
                    ? shaped.Weights(Mesh, placed, selection)
                    : selection.Vertices.ToDictionary(one => one, _ => 1f);
            }
        }

        public Vector3[] Translate(in Vector3 shift)
        {
            var points = (Vector3[])Mesh.Points.Clone();

            foreach (var (vertex, weight) in weights)
                points[vertex] = Vector3.Transform(placed[vertex] + shift * weight, toLocal);

            return points;
        }

        public Vector3[] Rotate(in Vector3 axis, float radians)
        {
            var points = (Vector3[])Mesh.Points.Clone();

            if (axis.LengthSquared() < 1e-12f)
                return points;

            var direction = Vector3.Normalize(axis);

            foreach (var (vertex, weight) in weights)
            {
                var center = OriginOf(vertex);
                var turn = Quaternion.CreateFromAxisAngle(direction, radians * weight);

                points[vertex] = Vector3.Transform(center + Vector3.Transform(placed[vertex] - center, turn), toLocal);
            }

            return points;
        }

        public Vector3[] Scale(in Vector3 factors, in Matrix4x4 basis)
        {
            var points = (Vector3[])Mesh.Points.Clone();

            Matrix4x4.Invert(basis, out var unturn);

            foreach (var (vertex, weight) in weights)
            {
                var center = OriginOf(vertex);
                var scale = Vector3.Lerp(Vector3.One, factors, weight);
                var along = Vector3.TransformNormal(placed[vertex] - center, unturn) * scale;

                points[vertex] = Vector3.Transform(center + Vector3.TransformNormal(along, basis), toLocal);
            }

            return points;
        }

        public Vector3[] Push(Vector3[] normals, float amount)
        {
            var points = (Vector3[])Mesh.Points.Clone();

            foreach (var (vertex, weight) in weights)
                points[vertex] = Mesh.Points[vertex] + normals[vertex] * amount * weight;

            return points;
        }

        private Vector3 OriginOf(int vertex) => individual && origins.TryGetValue(vertex, out var own) ? own : Pivot;
    }
}
