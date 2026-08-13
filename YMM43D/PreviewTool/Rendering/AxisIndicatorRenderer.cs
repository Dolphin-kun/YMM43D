using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class AxisIndicatorRenderer : IDisposable
    {
        private static readonly Color4 AxisXColor = new(1f, 0.3f, 0.35f, 1f);
        private static readonly Color4 AxisYColor = new(0.45f, 0.9f, 0.35f, 1f);
        private static readonly Color4 AxisZColor = new(0.35f, 0.6f, 1f, 1f);

        private const float PositiveArm = 1f;

        private const float NegativeArm = 0.45f;

        private const float CapRadius = 0.17f;

        private const float ViewSize = 2.7f;

        private const float ViewDistance = 4f;

        private const float SizeRatio = 0.2f;

        private const float MinSize = 52f;
        private const float MaxSize = 108f;

        private const float Margin = 10f;

        private readonly DeviceResourceCache<IndicatorResources> resources;

        public AxisIndicatorRenderer()
        {
            resources = new DeviceResourceCache<IndicatorResources>(
                device => new IndicatorResources(device));
        }

        public void Draw(in Render3DContext render, in CameraPose pose, float width, float height)
        {
            if (width <= 0f || height <= 0f)
                return;

            var size = Math.Clamp(MathF.Min(width, height) * SizeRatio, MinSize, MaxSize);
            if (size + Margin * 2f > width || size + Margin * 2f > height)
                return;

            var forward = Vector3.Transform(new Vector3(0f, 0f, -1f), pose.Rotation);
            var up = Vector3.Transform(Vector3.UnitY, pose.Rotation);

            var view = Matrix4x4.CreateLookAt(-forward * ViewDistance, Vector3.Zero, up);
            var projection = Matrix4x4.CreateOrthographic(ViewSize, ViewSize, 0.1f, ViewDistance * 2f);

            var shared = resources.Get(render.Device);
            var transform = view * projection;

            var settings = new DrawSettings { IgnoreDepth = true, SkipDepthWrite = true };

            render.Context.RSSetViewport(
                new Viewport(width - size - Margin, height - size - Margin, size, size));

            foreach (var mesh in shared.Axes)
                shared.Pipeline.Draw(render.Context, TransformConstants.CreateUnlit(transform, 1f), settings, mesh);

            render.Context.RSSetViewport(new Viewport(0, 0, width, height));
        }

        private static Vector3[] BuildAxis(Vector3 axis)
        {
            List<Vector3> points =
            [
                -axis * NegativeArm, axis * PositiveArm,
            ];

            points.AddRange(BuildCap(axis * PositiveArm));

            return [.. points];
        }

        private static IEnumerable<Vector3> BuildCap(Vector3 center)
        {
            Vector3[] tips =
            [
                new(CapRadius, 0f, 0f), new(-CapRadius, 0f, 0f),
                new(0f, CapRadius, 0f), new(0f, -CapRadius, 0f),
                new(0f, 0f, CapRadius), new(0f, 0f, -CapRadius),
            ];

            for (var i = 0; i < tips.Length; i++)
            {
                for (var j = i + 1; j < tips.Length; j++)
                {
                    if (tips[i] == -tips[j])
                        continue;

                    yield return center + tips[i];
                    yield return center + tips[j];
                }
            }
        }

        public void Dispose() => resources.Dispose();

        private sealed class IndicatorResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; } = new RenderPipeline<TransformConstants>(
                    device, Vertex.InputElements, new VertexColorMaterial(device));

            public LineMesh[] Axes { get; } =
                [
                    new LineMesh(device, BuildAxis(Vector3.UnitX), AxisXColor),
                    new LineMesh(device, BuildAxis(Vector3.UnitY), AxisYColor),
                    new LineMesh(device, BuildAxis(Vector3.UnitZ), AxisZColor),
                ];

            public void Dispose()
            {
                foreach (var mesh in Axes)
                    mesh.Dispose();

                Pipeline.Dispose();
            }
        }
    }
}
