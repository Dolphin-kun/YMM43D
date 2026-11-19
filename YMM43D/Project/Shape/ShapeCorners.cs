using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape
{
    internal static class ShapeCorners
    {
        public readonly record struct Corner(int Point, Vector3 Normal, Vector2 Uv);

        public static Corner[][] Of(SurfaceGeometry geometry)
        {
            var (min, max) = MeshUv.Bounds(geometry.Vertices);
            var size = Vector3.Max(max - min, new Vector3(1e-6f));
            var result = new Corner[geometry.Faces.Length][];

            for (var f = 0; f < geometry.Faces.Length; f++)
            {
                var face = geometry.Faces[f];
                var indices = face.Indices;
                var uvs = face.Uvs ?? MaterialSlotMesh.BoxUvs(geometry, indices, min, size);
                var outward = Outward(geometry, indices);

                result[f] = [.. indices.Select((index, i) => new Corner(index, face.IsSmooth ? geometry.Normals[index] : outward, uvs[i]))];
            }

            return result;
        }

        public static Corner[] Reversed(Corner[] corners) => [corners[0], .. corners.Skip(1).Reverse()];

        private static Vector3 Outward(SurfaceGeometry geometry, int[] indices)
        {
            var sum = Vector3.Zero;

            for (var i = 1; i + 1 < indices.Length; i++)
                sum += Vector3.Cross(geometry.Vertices[indices[i]] - geometry.Vertices[indices[0]], geometry.Vertices[indices[i + 1]] - geometry.Vertices[indices[0]]);

            return sum.LengthSquared() > 1e-20f ? -Vector3.Normalize(sum) : Vector3.UnitZ;
        }
    }
}
