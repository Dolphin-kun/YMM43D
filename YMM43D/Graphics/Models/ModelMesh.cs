using System.Numerics;
using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Models
{
    public sealed class ModelMesh : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public ModelData Source { get; }

        public Vector4 Tint { get; }

        public IReadOnlyList<ModelImage?> Replacements { get; }

        public IReadOnlyList<(IMesh Mesh, ModelTextures Textures, ModelPart Part)> Parts { get; }

        public ModelMesh(
            ID3D11Device device,
            ID3D11DeviceContext context,
            ModelData source,
            Vector4 tint,
            IReadOnlyList<ModelImage?>? replacements = null)
        {
            Source = source;
            Tint = tint;
            Replacements = replacements ?? [];

            var vertices = (Vertex[])source.Vertices.Clone();
            var painted = new bool[vertices.Length];

            foreach (var part in source.Parts)
            {
                var color = part.Color * tint;

                for (var i = part.IndexStart; i < part.IndexStart + part.IndexCount; i++)
                {
                    var index = source.Indices[i];

                    if (painted[index])
                        continue;

                    painted[index] = true;

                    ref var vertex = ref vertices[index];
                    vertex.Color = new Color4(vertex.Color.ToVector4() * color);
                }
            }

            var vertexBuffer = source.IsDeformed
                ? D3D11Buffers.Create(device, Skinned(vertices, source), BindFlags.VertexBuffer)
                : D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);

            disposer.Collect(vertexBuffer);

            var textures = new ID3D11ShaderResourceView?[source.Images.Length];

            for (var i = 0; i < textures.Length; i++)
                textures[i] = CreateTexture(device, context, source.Images[i]);

            var replaced = new ID3D11ShaderResourceView?[source.Materials.Length];

            for (var i = 0; i < replaced.Length && i < Replacements.Count; i++)
            {
                if (Replacements[i] is { } image)
                    replaced[i] = CreateTexture(device, context, image);
            }

            var parts = new List<(IMesh, ModelTextures, ModelPart)>();

            foreach (var part in source.Parts)
            {
                var indices = source.Indices.AsSpan(part.IndexStart, part.IndexCount).ToArray();
                var mesh = new PartMesh(
                    vertexBuffer,
                    D3D11Buffers.Create(device, indices, BindFlags.IndexBuffer),
                    indices.Length,
                    source.IsDeformed);
                disposer.Collect(mesh);

                var texture = part.Material >= 0 && part.Material < replaced.Length ? replaced[part.Material] : null;
                var surface = part.Surface;

                parts.Add((
                    mesh,
                    new ModelTextures(
                        texture ?? At(textures, part.Image),
                        At(textures, surface.BumpImage),
                        At(textures, surface.GlowImage),
                        At(textures, surface.FeelImage),
                        At(textures, surface.ShadeImage)),
                    part));
            }

            Parts = parts;
        }

        private static ID3D11ShaderResourceView? At(ID3D11ShaderResourceView?[] views, int index)
            => index >= 0 && index < views.Length ? views[index] : null;

        private static SkinnedVertex[] Skinned(Vertex[] vertices, ModelData source)
        {
            var skinned = new SkinnedVertex[vertices.Length];

            for (var i = 0; i < vertices.Length; i++)
            {
                var influence = source.HasBones ? source.Influences[i] : ModelInfluence.None;

                skinned[i] = new SkinnedVertex(
                    vertices[i],
                    influence.Bone0, influence.Bone1, influence.Bone2, influence.Bone3,
                    influence.Weights);
            }

            return skinned;
        }

        private ID3D11ShaderResourceView? CreateTexture(ID3D11Device device, ID3D11DeviceContext context, ModelImage image)
        {
            if (ModelTexture.Create(device, context, image) is not { } made)
                return null;

            disposer.Collect(made.Texture);
            disposer.Collect(made.View);

            return made.View;
        }

        public void Dispose() => disposer.Dispose();

        private sealed class PartMesh(
            ID3D11Buffer vertexBuffer, ID3D11Buffer indexBuffer, int count, bool skinned) : IMesh
        {
            public ID3D11Buffer VertexBuffer { get; } = vertexBuffer;

            public ID3D11Buffer? IndexBuffer { get; } = indexBuffer;

            public int DrawCount { get; } = count;

            public int VertexStride => skinned ? SkinnedVertex.Stride : Vertex.Stride;

            public InputElementDescription[] InputElements
                => skinned ? SkinnedVertex.InputElements : Vertex.InputElements;

            public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

            public Format IndexFormat => Format.R32_UInt;

            public void Dispose() => IndexBuffer?.Dispose();
        }
    }
}
