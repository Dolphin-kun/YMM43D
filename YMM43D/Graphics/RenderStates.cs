using D3DBlend = Vortice.Direct3D11.Blend;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics
{
    public sealed class RenderStates : IDisposable
    {
        private static readonly DeviceResourceCache<RenderStates> shared = new(device => new RenderStates(device));

        public static RenderStates For(ID3D11Device device) => shared.Get(device);

        private readonly DisposeCollector disposer = new();

        private readonly ID3D11BlendState normal;
        private readonly ID3D11BlendState noColorWrite;
        private readonly ID3D11BlendState add;
        private readonly ID3D11BlendState subtract;
        private readonly ID3D11BlendState multiply;
        private readonly ID3D11BlendState screen;

        public ID3D11DepthStencilState DepthDefault { get; }

        public ID3D11DepthStencilState DepthDisabled { get; }

        public ID3D11DepthStencilState DepthTestOnly { get; }

        public ID3D11RasterizerState CullNone { get; }

        public ID3D11RasterizerState CullBack { get; }

        public ID3D11RasterizerState CullFront { get; }

        public ID3D11SamplerState LinearSampler { get; }

        public ID3D11SamplerState PointSampler { get; }

        public RenderStates(ID3D11Device device)
        {
            normal = CreateBlend(device, D3DBlend.SourceAlpha, D3DBlend.InverseSourceAlpha, BlendOperation.Add);
            add = CreateBlend(device, D3DBlend.SourceAlpha, D3DBlend.One, BlendOperation.Add);
            subtract = CreateBlend(device, D3DBlend.SourceAlpha, D3DBlend.One, BlendOperation.ReverseSubtract);
            multiply = CreateBlend(device, D3DBlend.DestinationColor, D3DBlend.InverseSourceAlpha, BlendOperation.Add);
            screen = CreateBlend(device, D3DBlend.One, D3DBlend.InverseSourceColor, BlendOperation.Add);

            noColorWrite = Collect(device.CreateBlendState(new BlendDescription
            {
                RenderTarget =
                {
                    [0] = new RenderTargetBlendDescription
                    {
                        IsBlendEnabled = false,
                        RenderTargetWriteMask = ColorWriteEnable.None,
                    },
                },
            }));

            DepthDefault = Collect(device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthFunc = ComparisonFunction.LessEqual,
            }));

            DepthDisabled = Collect(device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = false,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunction.Always,
            }));

            DepthTestOnly = Collect(device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunction.LessEqual,
            }));

            CullNone = CreateRasterizer(device, CullMode.None);
            CullBack = CreateRasterizer(device, CullMode.Back);
            CullFront = CreateRasterizer(device, CullMode.Front);

            LinearSampler = CreateSampler(device, Filter.MinMagMipLinear);
            PointSampler = CreateSampler(device, Filter.MinMagMipPoint);
        }

        public ID3D11BlendState NoColorWrite => noColorWrite;

        public ID3D11BlendState GetBlend(BlendMode mode) => mode switch
        {
            BlendMode.Add => add,
            BlendMode.Subtract => subtract,
            BlendMode.Multiply => multiply,
            BlendMode.Screen => screen,
            _ => normal,
        };

        private ID3D11BlendState CreateBlend(ID3D11Device device, D3DBlend source, D3DBlend destination, BlendOperation operation)
        {
            var desc = new BlendDescription();
            desc.RenderTarget[0] = new RenderTargetBlendDescription
            {
                IsBlendEnabled = true,
                RenderTargetWriteMask = ColorWriteEnable.All,
                SourceBlend = source,
                DestinationBlend = destination,
                BlendOperation = operation,
                SourceBlendAlpha = D3DBlend.One,
                DestinationBlendAlpha = D3DBlend.InverseSourceAlpha,
                BlendOperationAlpha = BlendOperation.Add,
            };
            return Collect(device.CreateBlendState(desc));
        }

        private ID3D11RasterizerState CreateRasterizer(ID3D11Device device, CullMode cullMode)
        {
            return Collect(device.CreateRasterizerState(new RasterizerDescription
            {
                FillMode = FillMode.Solid,
                CullMode = cullMode,
                DepthClipEnable = true,
                MultisampleEnable = true,
                AntialiasedLineEnable = true,
                ScissorEnable = false,
            }));
        }

        private ID3D11SamplerState CreateSampler(ID3D11Device device, Filter filter)
        {
            return Collect(device.CreateSamplerState(new SamplerDescription
            {
                Filter = filter,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
            }));
        }

        private T Collect<T>(T resource) where T : IDisposable
        {
            disposer.Collect(resource);
            return resource;
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
