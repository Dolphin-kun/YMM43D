using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class SelectionRenderer : IDisposable
    {
        private static readonly Color4 OutlineColor = new(0.35f, 0.75f, 1f, 1f);

        private const float MinThickness = 0.01f;

        private readonly DeviceResourceCache<OutlineResources> resources = new(device => new OutlineResources(device));

        public void Draw(in Render3DContext render, in WorldBounds bounds, in Matrix4x4 world)
        {
            var size = Vector3.Max(bounds.Max - bounds.Min, new Vector3(MinThickness));
            var box = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(bounds.Center) * world;

            var shared = resources.Get(render.Device);

            shared.Pipeline.Draw(
                render.Context,
                TransformConstants.CreateUnlit(render.GetWorldViewProjection(box), 0.9f),
                new DrawSettings { IgnoreDepth = true },
                shared.Outline);
        }

        public void Dispose() => resources.Dispose();

        private static Vector3[] BuildOutline()
        {
            var corners = WorldBounds.FromCube(1f).GetCorners();

            (int From, int To)[] edges =
            [
                (0, 1), (2, 3), (4, 5), (6, 7),
                (0, 2), (1, 3), (4, 6), (5, 7),
                (0, 4), (1, 5), (2, 6), (3, 7),
            ];

            return [.. edges.SelectMany(edge => new[] { corners[edge.From], corners[edge.To] })];
        }

        private sealed class OutlineResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            public LineMesh Outline { get; } = new(device, BuildOutline(), OutlineColor);

            public void Dispose()
            {
                Outline.Dispose();
                Pipeline.Dispose();
            }
        }
    }
}
