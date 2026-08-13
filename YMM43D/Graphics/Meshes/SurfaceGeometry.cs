using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public readonly record struct SurfaceFace(int[] Indices, int Group, bool IsSmooth);

    public sealed record SurfaceGeometry(
        Vector3[] Vertices,
        Vector3[] Normals,
        SurfaceFace[] Faces,
        int GroupCount)
    {
        public static SurfaceGeometry Faceted(Vector3[] vertices, int[][] faces)
        {
            var built = new SurfaceFace[faces.Length];

            for (var i = 0; i < faces.Length; i++)
                built[i] = new SurfaceFace(faces[i], i, false);

            return new SurfaceGeometry(vertices, new Vector3[vertices.Length], built, faces.Length)
                .FacingOutward();
        }

        public SurfaceGeometry ScaledToUnit()
        {
            var extent = 0f;

            foreach (var vertex in Vertices)
            {
                extent = MathF.Max(
                    extent,
                    MathF.Max(MathF.Abs(vertex.X), MathF.Max(MathF.Abs(vertex.Y), MathF.Abs(vertex.Z))));
            }

            if (extent <= 0f || MathF.Abs(extent - 0.5f) < 1e-6f)
                return this;

            var scale = 0.5f / extent;
            var scaled = new Vector3[Vertices.Length];

            for (var i = 0; i < Vertices.Length; i++)
                scaled[i] = Vertices[i] * scale;

            return this with { Vertices = scaled };
        }

        public SurfaceGeometry FacingOutward()
        {
            foreach (var face in Faces)
            {
                if (Winding(face) > 0f)
                    Array.Reverse(face.Indices);
            }

            return this;
        }

        private float Winding(in SurfaceFace face)
        {
            var indices = face.Indices;

            var turn = Vector3.Cross(
                Vertices[indices[1]] - Vertices[indices[0]],
                Vertices[indices[2]] - Vertices[indices[0]]);

            var outward = Vector3.Zero;

            foreach (var index in indices)
                outward += Normals[index].LengthSquared() > 0f ? Normals[index] : Vertices[index];

            return Vector3.Dot(turn, outward);
        }
    }
}
