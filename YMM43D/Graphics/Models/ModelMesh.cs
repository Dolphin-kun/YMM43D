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

        public IReadOnlyList<(IMesh Mesh, ID3D11ShaderResourceView? Texture)> Parts { get; }

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

            var vertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
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

            var parts = new List<(IMesh, ID3D11ShaderResourceView?)>();

            foreach (var part in source.Parts)
            {
                var indices = source.Indices.AsSpan(part.IndexStart, part.IndexCount).ToArray();
                var mesh = new PartMesh(vertexBuffer, D3D11Buffers.Create(device, indices, BindFlags.IndexBuffer), indices.Length);
                disposer.Collect(mesh);

                var texture = part.Material >= 0 && part.Material < replaced.Length ? replaced[part.Material] : null;

                parts.Add((mesh, texture ?? (part.Image >= 0 ? textures[part.Image] : null)));
            }

            Parts = parts;
        }

        private ID3D11ShaderResourceView? CreateTexture(ID3D11Device device, ID3D11DeviceContext context, ModelImage image)
        {
            if (image.Width <= 0 || image.Height <= 0)
                return null;

            var texture = device.CreateTexture2D(new Texture2DDescription
            {
                Width = image.Width,
                Height = image.Height,
                MipLevels = 0,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                MiscFlags = ResourceOptionFlags.GenerateMips,
            });
            disposer.Collect(texture);

            context.UpdateSubresource(image.Pixels, texture, 0, image.Width * 4);

            var view = device.CreateShaderResourceView(texture);
            disposer.Collect(view);

            context.GenerateMips(view);

            return view;
        }

        public void Dispose() => disposer.Dispose();

        private sealed class PartMesh(ID3D11Buffer vertexBuffer, ID3D11Buffer indexBuffer, int count) : IMesh
        {
            public ID3D11Buffer VertexBuffer { get; } = vertexBuffer;

            public ID3D11Buffer? IndexBuffer { get; } = indexBuffer;

            public int DrawCount { get; } = count;

            public int VertexStride => Vertex.Stride;

            public InputElementDescription[] InputElements => Vertex.InputElements;

            public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

            public Format IndexFormat => Format.R32_UInt;

            public void Dispose() => IndexBuffer?.Dispose();
        }
    }
}
