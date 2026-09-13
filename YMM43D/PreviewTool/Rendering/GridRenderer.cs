using System.Numerics;
using System.Runtime.InteropServices;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics;
using YMM43D.Commons;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class GridRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GridConstants
        {
            public Matrix4x4 WorldViewProjection;
            public Vector4 CameraPosition;
        }

        private readonly DeviceResourceCache<RenderPipeline<GridConstants>> pipelines;

        public GridRenderer()
        {
            pipelines = new DeviceResourceCache<RenderPipeline<GridConstants>>(
                device => new RenderPipeline<GridConstants>(
                    device,
                    new GroundPlaneMesh(device),
                    new ShaderMaterial(device, typeof(GridRenderer).Assembly, "Grid.hlsl")));
        }

        public void Draw(in Render3DContext render, Vector3 cameraPosition)
        {
            var constants = new GridConstants
            {
                WorldViewProjection = Matrix4x4.Transpose(render.ViewProjection),
                CameraPosition = new Vector4(cameraPosition, 0),
            };

            pipelines.Get(render.Device).Draw(render.Context, constants, new DrawSettings
            {
                Blend = BlendMode.Normal,
                Culling = FaceCulling.None,
            });
        }

        public void Dispose() => pipelines.Dispose();
    }
}
