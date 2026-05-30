using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;
using YukkuriMovieMaker.Commons;
using YMM43D.Commons;

namespace YMM43D.Rendering
{
    public class GridResources : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        [StructLayout(LayoutKind.Sequential)]
        public struct ConstantData
        {
            public Matrix4x4 WorldViewProjection;
            public Vector4 CameraPos;
        }

        public GridGeometry Geometry { get; }
        public GridMaterial Material { get; }
        public ID3D11InputLayout InputLayout { get; }
        public ID3D11Buffer ConstantBuffer { get; }
        public ID3D11BlendState BlendState { get; }
        public ID3D11RasterizerState RasterizerState { get; }

        public GridResources(ID3D11Device device)
        {
            Geometry = new GridGeometry(device);
            disposer.Collect(Geometry);
            Material = new GridMaterial(device);
            disposer.Collect(Material);

            InputLayout = device.CreateInputLayout(
                [
                    new("POSITION", 0, Vortice.DXGI.Format.R32G32B32_Float, 0, 0),
                    new("COLOR",    0, Vortice.DXGI.Format.R32G32B32A32_Float, 12, 0),
                    new("TEXCOORD", 0, Vortice.DXGI.Format.R32G32_Float, 28, 0)
                ], 
                Material.VertexShaderBytecode);
            disposer.Collect(InputLayout);

            ConstantBuffer = D3D11Helper.CreateConstantBuffer<ConstantData>(device);
            disposer.Collect(ConstantBuffer);

            var blendDesc = new BlendDescription();
            blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
            {
                IsBlendEnabled = true,
                SourceBlend = Blend.SourceAlpha,
                DestinationBlend = Blend.InverseSourceAlpha,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.Zero,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };
            BlendState = device.CreateBlendState(blendDesc);
            disposer.Collect(BlendState);

            var rasterDesc = new RasterizerDescription
            {
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
                DepthClipEnable = true
            };
            RasterizerState = device.CreateRasterizerState(rasterDesc);
            disposer.Collect(RasterizerState);
        }

        public void Dispose()
        {
            disposer.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
