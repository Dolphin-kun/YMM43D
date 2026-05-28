using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using YMM43D.Rendering;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;
using YMM43D.Rendering.States;
using YukkuriMovieMaker.Commons;
using YMM43D.Commons;

namespace YMM43D.Preview
{
    public class StandardVideoItemProvider : I3DProvider, IDisposable
    {
        public bool RequiresMappedTexture => true;

        private bool isDisposed;

        private class Resources : System.IDisposable
        {
            private readonly DisposeCollector disposer = new();

            public PlaneGeometry Geometry { get; }
            public TextureMaterial Material { get; }
            public ID3D11InputLayout InputLayout { get; }
            public ID3D11Buffer ConstantBuffer { get; }
            public BlendStates BlendStates { get; }
            public DepthStencilStates DepthStencilStates { get; }
            public SamplerStates SamplerStates { get; }
            public RasterizerStates RasterizerStates { get; }
            public ID3D11ShaderResourceView WhiteTexture { get; }

            public Resources(ID3D11Device device)
            {
                Geometry = new PlaneGeometry(device);
                disposer.Collect(Geometry);
                Material = new TextureMaterial(device);
                disposer.Collect(Material);
                InputLayout = device.CreateInputLayout(Geometry.InputElements, Material.VertexShaderBytecode);
                disposer.Collect(InputLayout);
                ConstantBuffer = D3D11Helper.CreateConstantBuffer<TextureMaterial.TransformBuffer>(device);
                disposer.Collect(ConstantBuffer);
                BlendStates = new BlendStates(device);
                disposer.Collect(BlendStates);
                DepthStencilStates = new DepthStencilStates(device);
                disposer.Collect(DepthStencilStates);
                SamplerStates = new SamplerStates(device);
                disposer.Collect(SamplerStates);
                RasterizerStates = new RasterizerStates(device);
                disposer.Collect(RasterizerStates);
                WhiteTexture = CreateWhiteTexture(device);
            }

            private unsafe ID3D11ShaderResourceView CreateWhiteTexture(ID3D11Device device)
            {
                var desc = new Texture2DDescription
                {
                    Width = 1,
                    Height = 1,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Vortice.DXGI.Format.R8G8B8A8_UNorm,
                    SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource
                };

                uint white = 0xFFFFFFFF;
                var texture = device.CreateTexture2D(desc, [new SubresourceData((nint)(&white), 4)]);
                disposer.Collect(texture);
                var srv = device.CreateShaderResourceView(texture);
                disposer.Collect(srv);
                return srv;
            }

            public void Dispose()
            {
                disposer.Dispose();
            }
        }

        private readonly DeviceResourceCache<Resources> resourceCache = new(device => new Resources(device));

        public void Draw(ID3D11Device device, ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            var res = resourceCache.Get(device);

            var wvp = drawContext.World * view * projection;
            var data = new TextureMaterial.TransformBuffer
            {
                WorldViewProjection = Matrix4x4.Transpose(wvp),
                Opacity = drawContext.Opacity
            };
            context.UpdateSubresource(in data, res.ConstantBuffer);

            context.OMSetBlendState(drawContext.Blend switch
            {
                YukkuriMovieMaker.Project.Blend.Add => res.BlendStates.Add,
                YukkuriMovieMaker.Project.Blend.Subtract => res.BlendStates.Subtract,
                YukkuriMovieMaker.Project.Blend.Multiply => res.BlendStates.Multiply,
                YukkuriMovieMaker.Project.Blend.Screen => res.BlendStates.Screen,
                _ => res.BlendStates.Normal
            });

            context.OMSetDepthStencilState(drawContext.IsAlwaysOnTop ? res.DepthStencilStates.NoDepth : res.DepthStencilStates.Default);
            context.RSSetState(res.RasterizerStates.CullNone); // 両面表示

            context.VSSetShader(res.Material.VertexShader);
            context.PSSetShader(res.Material.PixelShader);
            context.IASetInputLayout(res.InputLayout);
            context.IASetVertexBuffer(0, res.Geometry.VertexBuffer, Marshal.SizeOf<Vertex>(), 0);
            context.IASetIndexBuffer(res.Geometry.IndexBuffer, Vortice.DXGI.Format.R16_UInt, 0);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            
            context.VSSetConstantBuffer(0, res.ConstantBuffer);
            context.PSSetConstantBuffer(0, res.ConstantBuffer);

            context.PSSetShaderResource(0, drawContext.Texture ?? res.WhiteTexture);
            context.PSSetSampler(0, res.SamplerStates.Linear);

            context.DrawIndexed(res.Geometry.IndexCount, 0, 0);

            context.RSSetState(null);
            context.OMSetBlendState(null);
            context.OMSetDepthStencilState(null);
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            resourceCache.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
