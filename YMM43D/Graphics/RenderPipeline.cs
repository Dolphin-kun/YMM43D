using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics
{
    public sealed class RenderPipeline<TConstants> : IDisposable where TConstants : unmanaged
    {
        private readonly DisposeCollector disposer = new();
        private readonly ID3D11Device device;
        private readonly ID3D11InputLayout inputLayout;
        private readonly ID3D11Buffer constantBuffer;
        private readonly RenderStates states;

        private ID3D11Buffer? extraBuffer;
        private Type? extraType;

        public IMesh? Mesh { get; }

        public IMaterial Material { get; }

        public RenderPipeline(ID3D11Device device, IMesh mesh, IMaterial material, RenderStates? states = null)
            : this(device, mesh.InputElements, material, states)
        {
            Mesh = mesh;
            disposer.Collect(mesh);
        }

        public RenderPipeline(
            ID3D11Device device,
            InputElementDescription[] inputElements,
            IMaterial material,
            RenderStates? states = null)
        {
            Material = material;
            disposer.Collect(material);
            this.device = device;
            this.states = states ?? RenderStates.For(device);

            inputLayout = device.CreateInputLayout(inputElements, material.VertexShaderBytecode);
            disposer.Collect(inputLayout);
            constantBuffer = D3D11Buffers.CreateConstantBuffer<TConstants>(device);
            disposer.Collect(constantBuffer);
        }

        public void Draw<TExtra>(
            ID3D11DeviceContext context,
            in TConstants constants,
            in TExtra extra,
            in DrawSettings settings,
            IMesh? mesh = null)
            where TExtra : unmanaged
        {
            if (extraBuffer is null || extraType != typeof(TExtra))
            {
                disposer.RemoveAndDispose(ref extraBuffer);

                extraBuffer = D3D11Buffers.CreateConstantBuffer<TExtra>(device);
                disposer.Collect(extraBuffer);
                extraType = typeof(TExtra);
            }

            context.UpdateSubresource(in extra, extraBuffer);
            context.VSSetConstantBuffer(1, extraBuffer);
            context.PSSetConstantBuffer(1, extraBuffer);

            Draw(context, constants, settings, mesh);
        }

        public void Draw(
            ID3D11DeviceContext context,
            in TConstants constants,
            in DrawSettings settings,
            IMesh? mesh = null)
        {
            mesh ??= Mesh ?? throw new InvalidOperationException(
                "形状を持たないパイプラインです。描画する形状を渡してください。");

            context.UpdateSubresource(in constants, constantBuffer);

            context.OMSetBlendState(
                settings.DepthOnly ? states.NoColorWrite : states.GetBlend(settings.Blend));

            context.OMSetDepthStencilState(DepthStateFor(settings));

            context.RSSetState(settings.Culling switch
            {
                FaceCulling.Back => states.CullBack,
                FaceCulling.Front => states.CullFront,
                _ => states.CullNone,
            });

            context.VSSetShader(Material.VertexShader);
            context.PSSetShader(Material.PixelShader);
            context.VSSetConstantBuffer(0, constantBuffer);
            context.PSSetConstantBuffer(0, constantBuffer);

            context.IASetInputLayout(inputLayout);
            context.IASetVertexBuffer(0, mesh.VertexBuffer, mesh.VertexStride, 0);
            context.IASetPrimitiveTopology(mesh.Topology);

            if (settings.Texture is not null)
            {
                context.PSSetShaderResource(0, settings.Texture);
                context.PSSetSampler(0, settings.Sampler ?? states.LinearSampler);
            }

            if (mesh.IndexBuffer is not null)
            {
                context.IASetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat, 0);
                context.DrawIndexed(mesh.DrawCount, 0, 0);
            }
            else
            {
                context.Draw(mesh.DrawCount, 0);
            }

            ResetState(context, settings);
        }

        private ID3D11DepthStencilState DepthStateFor(in DrawSettings settings)
        {
            if (settings.DepthOnly)
                return states.DepthDefault;

            if (settings.IgnoreDepth)
                return states.DepthDisabled;

            return settings.SkipDepthWrite ? states.DepthTestOnly : states.DepthDefault;
        }

        private static void ResetState(ID3D11DeviceContext context, in DrawSettings settings)
        {
            if (settings.Texture is not null)
                context.PSSetShaderResource(0, null!);

            context.OMSetBlendState(null);
            context.OMSetDepthStencilState(null);
            context.RSSetState(null);
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
