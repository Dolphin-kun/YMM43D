using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;

namespace YMM43D.Rendering
{
    public class GridResources : IDisposable
    {
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
        public ID3D11RasterizerState RasterizerState { get; } // 追加

        public GridResources(ID3D11Device device)
        {
            Geometry = new GridGeometry(device);
            Material = new GridMaterial(device);

            InputLayout = device.CreateInputLayout(
                [
                    new("POSITION", 0, Vortice.DXGI.Format.R32G32B32_Float, 0, 0),
                    new("COLOR",    0, Vortice.DXGI.Format.R32G32B32A32_Float, 12, 0),
                    new("TEXCOORD", 0, Vortice.DXGI.Format.R32G32_Float, 28, 0)
                ], 
                Material.VertexShaderBytecode);

            ConstantBuffer = D3D11Helper.CreateConstantBuffer<ConstantData>(device);

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

            // カリングを無効にする設定 (両面描画用)
            var rasterDesc = new RasterizerDescription
            {
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
                DepthClipEnable = true
            };
            RasterizerState = device.CreateRasterizerState(rasterDesc);
            
            SharedGraphics.RegisterForCleanup(this);
        }

        public void Dispose()
        {
            RasterizerState.Dispose();
            BlendState.Dispose();
            ConstantBuffer.Dispose();
            InputLayout.Dispose();
            Material.Dispose();
            Geometry.Dispose();
        }
    }
}
