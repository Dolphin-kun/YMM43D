using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class OutlineRenderer : IDisposable
    {
        private static readonly Color4 PointColor = new(1f, 0.62f, 0.16f, 1f);

        public const float Ratio = 0.018f;

        public const float GrabThreshold = 10f;

        private readonly DeviceResourceCache<OutlineResources> resources;

        public OutlineRenderer()
        {
            resources = new DeviceResourceCache<OutlineResources>(device => new OutlineResources(device));
        }

        public void Draw(in Render3DContext render, IReadOnlyList<Vector3> points, in Vector3 cameraPosition)
        {
            if (points.Count == 0 || !resources.TryGet(render.Device, out var shared))
                return;

            var settings = new DrawSettings { IgnoreDepth = true };

            foreach (var point in points)
            {
                var size = MathF.Max(0.01f, Vector3.Distance(point, cameraPosition) * Ratio);
                var world = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(point);

                shared.Pipeline.Draw(
                    render.Context,
                    TransformConstants.CreateUnlit(render.GetWorldViewProjection(world), 1f),
                    settings,
                    shared.Marker);
            }
        }

        public void Dispose() => resources.Dispose();

        private static Vector3[] BuildMarker()
        {
            Vector3[] tips =
            [
                Vector3.UnitX, -Vector3.UnitX,
                Vector3.UnitY, -Vector3.UnitY,
                Vector3.UnitZ, -Vector3.UnitZ,
            ];

            var points = new List<Vector3>();

            for (var i = 0; i < tips.Length; i++)
            {
                for (var j = 0; j < tips.Length; j++)
                {
                    if (i == j || tips[i] == -tips[j])
                        continue;

                    points.Add(tips[i]);
                    points.Add(tips[j]);
                }
            }

            return [.. points];
        }

        private sealed class OutlineResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            public LineMesh Marker { get; } = new(device, BuildMarker(), PointColor);

            public void Dispose()
            {
                Marker.Dispose();
                Pipeline.Dispose();
            }
        }
    }
}
