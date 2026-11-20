using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Meshes;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    internal sealed class MaterialSlotMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }

        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount { get; }

        public int VertexStride => Vertex.Stride;

        public InputElementDescription[] InputElements => Vertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        private MaterialSlotMesh(ID3D11Device device, Vertex[] vertices)
        {
            DrawCount = vertices.Length;
            VertexBuffer = disposer.CollectEx(D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer));
        }

        public static MaterialSlotMesh?[] Build(ID3D11Device device, SurfaceGeometry geometry, IReadOnlyList<Color4> colors)
        {
            var count = Math.Max(colors.Count, 1);
            var triangles = new List<(int Corner, Vector3 Normal, Vector2 Uv)[]>[count];

            for (var slot = 0; slot < count; slot++)
                triangles[slot] = [];

            var tangents = new Dictionary<(int Slot, int Vertex), (Vector3 Along, Vector3 Across)>();
            var (min, max) = MeshUv.Bounds(geometry.Vertices);
            var size = Vector3.Max(max - min, new Vector3(1e-6f));

            foreach (var face in geometry.Faces)
            {
                var slot = face.Group >= 0 && face.Group < count ? face.Group : 0;
                var indices = face.Indices;
                var uvs = face.Uvs ?? BoxUvs(geometry, indices, min, size);

                for (var i = 1; i + 1 < indices.Length; i++)
                {
                    int[] slots = [0, i, i + 1];
                    var flat = -Vertex.GetNormal(geometry.Vertices[indices[0]], geometry.Vertices[indices[i]], geometry.Vertices[indices[i + 1]]);
                    var triangle = slots.Select(at => (indices[at], face.IsSmooth ? geometry.Normals[indices[at]] : flat, uvs[at])).ToArray();
                    var (along, across) = TangentOf(geometry, triangle);

                    foreach (var (corner, _, _) in triangle)
                    {
                        var (knownAlong, knownAcross) = tangents.GetValueOrDefault((slot, corner));

                        tangents[(slot, corner)] = (knownAlong + along, knownAcross + across);
                    }

                    triangles[slot].Add(triangle);
                }
            }

            var meshes = new MaterialSlotMesh?[count];

            for (var slot = 0; slot < count; slot++)
            {
                if (triangles[slot].Count == 0)
                    continue;

                var color = slot < colors.Count ? colors[slot] : new Color4(1f, 1f, 1f, 1f);
                var vertices = new List<Vertex>();

                foreach (var triangle in triangles[slot])
                {
                    foreach (var (corner, normal, uv) in triangle)
                    {
                        var (summedAlong, summedAcross) = tangents.GetValueOrDefault((slot, corner));
                        var along = summedAlong - normal * Vector3.Dot(summedAlong, normal);
                        var tangent = along.LengthSquared() > 1e-12f ? Vector3.Normalize(along) : Vector3.UnitX;
                        var hand = Vector3.Dot(Vector3.Cross(normal, tangent), summedAcross) < 0f ? -1f : 1f;

                        vertices.Add(new Vertex(geometry.Vertices[corner], color, uv, normal, new Vector4(tangent, hand)));
                    }
                }

                meshes[slot] = new MaterialSlotMesh(device, [.. vertices]);
            }

            return meshes;
        }

        internal static Vector2[] BoxUvs(SurfaceGeometry geometry, int[] indices, Vector3 min, Vector3 size)
        {
            var sum = Vector3.Zero;

            for (var i = 1; i + 1 < indices.Length; i++)
                sum += Vector3.Cross(geometry.Vertices[indices[i]] - geometry.Vertices[indices[0]], geometry.Vertices[indices[i + 1]] - geometry.Vertices[indices[0]]);

            return [.. indices.Select(index => MeshUv.BoxUv(geometry.Vertices[index], -sum, min, size))];
        }

        private static (Vector3 Along, Vector3 Across) TangentOf(SurfaceGeometry geometry, (int Corner, Vector3 Normal, Vector2 Uv)[] triangle)
        {
            var (p0, p1, p2) = (geometry.Vertices[triangle[0].Corner], geometry.Vertices[triangle[1].Corner], geometry.Vertices[triangle[2].Corner]);
            var (d1, d2) = (triangle[1].Uv - triangle[0].Uv, triangle[2].Uv - triangle[0].Uv);
            var determinant = d1.X * d2.Y - d2.X * d1.Y;

            if (MathF.Abs(determinant) < 1e-12f)
                return (Vector3.Zero, Vector3.Zero);

            return (((p1 - p0) * d2.Y - (p2 - p0) * d1.Y) / determinant, ((p2 - p0) * d1.X - (p1 - p0) * d2.X) / determinant);
        }

        public void Dispose() => disposer.Dispose();
    }
}
