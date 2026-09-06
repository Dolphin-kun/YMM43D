using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

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
                    new GridMaterial(device)));
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

    internal sealed class GridMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        private const string Shader = "Grid.hlsl";

        public GridMaterial(ID3D11Device device)
        {
            var assembly = typeof(GridMaterial).Assembly;

            VertexShaderBytecode = ShaderLibrary.Compile(assembly, Shader, "VSMain", "vs_5_0");
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            PixelShader = device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, Shader, "PSMain", "ps_5_0"));
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
