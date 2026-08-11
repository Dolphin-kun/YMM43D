using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Rendering;
using YMM43D.Rendering.Materials;
using YMM43D.Commons;

namespace YMM43D.Preview
{
    public class DefaultItemProvider : I3DProvider, IDisposable
    {
        public bool RequiresMappedTexture => true;

        private readonly DeviceResourceCache<DefaultItemResources> resourceCache = new(device => new DefaultItemResources(device));
        private bool isDisposed;

        public void Draw(ID3D11Device device, ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            if (drawContext.Texture == null) return;

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
            context.IASetIndexBuffer(res.Geometry.IndexBuffer, Format.R16_UInt, 0);
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
