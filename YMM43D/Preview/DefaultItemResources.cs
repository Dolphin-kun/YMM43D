
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;
using YMM43D.Rendering.States;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Preview
{
    public class DefaultItemResources : IDisposable
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

        public DefaultItemResources(ID3D11Device device)
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
            GC.SuppressFinalize(this);
        }
    }
}
