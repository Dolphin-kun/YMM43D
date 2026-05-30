using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Rendering;
using YMM43D.Rendering.States;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace Extrusion3D
{
    internal class Extrusion3DSource : IDisposable
    {
        private readonly Extrusion3DEffect effect;
        internal readonly Extrusion3DProcessor? processor;
        private readonly DeviceResourceCache<ExtrusionResources> resourceCache;

        public Extrusion3DSource(Extrusion3DEffect effect, Extrusion3DProcessor? processor)
        {
            this.effect = effect;
            this.processor = processor;
            this.resourceCache = new DeviceResourceCache<ExtrusionResources>(device => new ExtrusionResources(device));
        }

        public void Draw(ID3D11Device device, ID3D11DeviceContext d3dDc, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            var texture = processor?.GetTexture(device) ?? effect.GetTexture(device);
            if (texture == null) return;
            
            var res = resourceCache.Get(device);
            float thickness = (float)(effect.Thickness.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) / 100.0);
            if (thickness <= 0) return;

            var extrusionMatrix = Matrix4x4.CreateScale(1.0f, 1.0f, thickness);
            var finalWorld = extrusionMatrix * drawContext.World;
            var wvpMatrix = finalWorld * view * projection;

            var sideColor = effect.SideColor;
            var sideColorVec = new Vector4(sideColor.R / 255f, sideColor.G / 255f, sideColor.B / 255f, sideColor.A / 255f);

            Matrix4x4.Invert(view, out var invView);
            var cameraWorldPos = invView.Translation;
            Matrix4x4.Invert(finalWorld, out var invWorld);
            var cameraLocalPos = Vector3.Transform(cameraWorldPos, invWorld);

            var attenuation = (float)(effect.Attenuation.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) / 100.0);

            var data = new ExtrusionResources.ConstantData
            {
                WorldViewProjection = Matrix4x4.Transpose(wvpMatrix),
                SideColor = sideColorVec,
                CameraLocalPos = cameraLocalPos,
                Opacity = drawContext.Opacity,
                ExtrusionType = (int)effect.ExtrusionType,
                Attenuation = attenuation
            };
            d3dDc.UpdateSubresource(in data, res.ConstantBuffer);

            var depthStates = res.DepthStencilStates.Get(device);
            d3dDc.OMSetDepthStencilState(drawContext.IsAlwaysOnTop ? depthStates.NoDepth : depthStates.Default);
            var blendStates = res.BlendStates.Get(device);
            d3dDc.OMSetBlendState(blendStates.Normal);
            var rasterStates = res.RasterizerStates.Get(device);

            d3dDc.VSSetShader(res.Material.VertexShader);
            d3dDc.PSSetShader(res.Material.PixelShader);
            d3dDc.IASetInputLayout(res.InputLayout);
            d3dDc.IASetVertexBuffer(0, res.Geometry.VertexBuffer, Marshal.SizeOf<Vertex>(), 0);
            d3dDc.IASetIndexBuffer(res.Geometry.IndexBuffer, Format.R16_UInt, 0);
            d3dDc.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            d3dDc.VSSetConstantBuffer(0, res.ConstantBuffer);
            d3dDc.PSSetConstantBuffer(0, res.ConstantBuffer);
            d3dDc.PSSetShaderResource(0, texture);
            d3dDc.PSSetSampler(0, res.SamplerState);

            d3dDc.RSSetState(rasterStates.CullFront);
            d3dDc.DrawIndexed(res.Geometry.IndexCount, 0, 0);

            d3dDc.RSSetState(null!);
            d3dDc.PSSetShaderResource(0, null!);
        }

        public void Dispose()
        {
            resourceCache.Dispose();
        }

        private class ExtrusionResources : IDisposable
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct ConstantData
            {
                public Matrix4x4 WorldViewProjection;
                public Vector4 SideColor;
                public Vector3 CameraLocalPos;
                public float Opacity;
                public int ExtrusionType;
                public float Attenuation;
                private Vector2 padding;
            }

            public I3DGeometry Geometry { get; }
            public I3DMaterial Material { get; }
            public ID3D11InputLayout InputLayout { get; }
            public ID3D11Buffer ConstantBuffer { get; }
            public ID3D11SamplerState SamplerState { get; }
            public DeviceResourceCache<BlendStates> BlendStates { get; }
            public DeviceResourceCache<DepthStencilStates> DepthStencilStates { get; }
            public DeviceResourceCache<RasterizerStates> RasterizerStates { get; }

            public ExtrusionResources(ID3D11Device device)
            {
                Geometry = new ExtrusionGeometry(device);
                Material = new ExtrusionMaterial(device);
                InputLayout = device.CreateInputLayout(Geometry.InputElements, Material.VertexShaderBytecode);
                ConstantBuffer = D3D11Helper.CreateConstantBuffer<ConstantData>(device);
                SamplerState = device.CreateSamplerState(new SamplerDescription {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                });

                BlendStates = new DeviceResourceCache<BlendStates>(d => new BlendStates(d));
                DepthStencilStates = new DeviceResourceCache<DepthStencilStates>(d => new DepthStencilStates(d));
                RasterizerStates = new DeviceResourceCache<RasterizerStates>(d => new RasterizerStates(d));
            }

            public void Dispose()
            {
                SamplerState.Dispose();
                ConstantBuffer.Dispose();
                InputLayout.Dispose();
                Material.Dispose();
                Geometry.Dispose();
                BlendStates.Dispose();
                DepthStencilStates.Dispose();
                RasterizerStates.Dispose();
            }
        }
    }

    internal class ExtrusionGeometry : I3DGeometry
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer IndexBuffer { get; }
        public int IndexCount => 36;
        public InputElementDescription[] InputElements { get; }

        public ExtrusionGeometry(ID3D11Device device)
        {
            InputElements = [
                new InputElementDescription("POSITION", 0, Vortice.DXGI.Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("COLOR", 0, Vortice.DXGI.Format.R32G32B32A32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Vortice.DXGI.Format.R32G32_Float, 28, 0)
            ];

            // 前面を Z = 0.0f（元の2D平面の位置）、背面を Z = 1.0f（奥方向）に配置
            var vertices = new[] {
                new Vertex(new(-0.5f,  0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(0f, 0f)), // 0: TL-Near
                new Vertex(new( 0.5f,  0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(1f, 0f)), // 1: TR-Near
                new Vertex(new(-0.5f, -0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(0f, 1f)), // 2: BL-Near
                new Vertex(new( 0.5f, -0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(1f, 1f)), // 3: BR-Near
                new Vertex(new(-0.5f,  0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(0f, 0f)), // 4: TL-Far
                new Vertex(new( 0.5f,  0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(1f, 0f)), // 5: TR-Far
                new Vertex(new(-0.5f, -0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(0f, 1f)), // 6: BL-Far
                new Vertex(new( 0.5f, -0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(1f, 1f)), // 7: BR-Far
            };

            ushort[] indices = [
                // 前面 (z=0.0)
                0, 1, 2,  1, 3, 2,
                // 背面 (z=1.0)
                5, 4, 7,  4, 6, 7,
                // 左面 (x=-0.5)
                4, 0, 6,  0, 2, 6,
                // 右面 (x=0.5)
                1, 5, 3,  5, 7, 3,
                // 上面 (y=0.5)
                4, 5, 0,  5, 1, 0,
                // 下面 (y=-0.5)
                2, 3, 6,  3, 7, 6
            ];

            VertexBuffer = D3D11Helper.CreateBuffer(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
            IndexBuffer = D3D11Helper.CreateBuffer(device, indices, BindFlags.IndexBuffer);
            disposer.Collect(IndexBuffer);
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
