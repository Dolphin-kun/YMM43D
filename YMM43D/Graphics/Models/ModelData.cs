using System.IO;
using System.Numerics;
using Vortice.Mathematics;
using YMM43D.Commons;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelPart(int IndexStart, int IndexCount, Vector4 Color, int Image);

    public sealed record ModelImage(int Width, int Height, byte[] Pixels);

    public sealed class ModelData(Vertex[] vertices, uint[] indices, ModelPart[] parts, ModelImage[] images)
    {
        public Vertex[] Vertices { get; } = vertices;

        public uint[] Indices { get; } = indices;

        public ModelPart[] Parts { get; } = parts;

        public ModelImage[] Images { get; } = images;

        public WorldBounds Bounds { get; } = Measure(vertices);

        private static WorldBounds Measure(Vertex[] vertices)
        {
            if (vertices.Length == 0)
                return WorldBounds.Empty;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var vertex in vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return new WorldBounds(min, max);
        }
    }

    internal sealed class ModelBuilder
    {
        private readonly List<Vertex> vertices = [];
        private readonly List<uint> indices = [];
        private readonly List<ModelPart> parts = [];
        private readonly List<ModelImage> images = [];

        private int partStart;
        private Vector4 partColor = Vector4.One;
        private int partImage = -1;

        public int VertexCount => vertices.Count;

        public int AddImage(ModelImage image)
        {
            images.Add(image);
            return images.Count - 1;
        }

        public void BeginPart(Vector4 color, int image)
        {
            EndPart();
            partColor = color;
            partImage = image;
        }

        public uint AddVertex(Vector3 position, Vector3 normal, Vector2 texCoord, Vector4 color)
        {
            vertices.Add(new Vertex(position, new Color4(color), texCoord, normal));
            return (uint)(vertices.Count - 1);
        }

        public void AddTriangle(uint a, uint b, uint c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        public Vector3 PositionOf(uint index) => vertices[(int)index].Position;

        public void SetNormal(uint index, Vector3 normal)
        {
            var vertex = vertices[(int)index];
            vertex.Normal = normal;
            vertices[(int)index] = vertex;
        }

        public ModelData Build()
        {
            EndPart();

            if (indices.Count == 0)
                throw new InvalidDataException("三角形が1つもありません。");

            return new ModelData([.. vertices], [.. indices], [.. parts], [.. images]);
        }

        private void EndPart()
        {
            if (indices.Count > partStart)
                parts.Add(new ModelPart(partStart, indices.Count - partStart, partColor, partImage));

            partStart = indices.Count;
        }
    }
}
