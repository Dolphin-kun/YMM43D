using D3DBlend = Vortice.Direct3D11.Blend;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;
using YMM43D.Commons;

namespace YMM43D.Graphics
{
    public sealed class RenderStates : IDisposable
    {
        private static readonly DeviceResourceCache<RenderStates> shared = new(device => new RenderStates(device));

        public static RenderStates For(ID3D11Device device) => shared.Get(device);

        private readonly DisposeCollector disposer = new();

        private readonly ID3D11BlendState normal;
        private readonly ID3D11BlendState add;
        private readonly ID3D11BlendState subtract;
        private readonly ID3D11BlendState multiply;
        private readonly ID3D11BlendState screen;
        private readonly ID3D11BlendState accumulate;

        public ID3D11DepthStencilState DepthDefault { get; }

        public ID3D11DepthStencilState DepthDisabled { get; }

        public ID3D11DepthStencilState DepthTestOnly { get; }

        public ID3D11RasterizerState CullNone { get; }

        public ID3D11RasterizerState CullBack { get; }

        public ID3D11RasterizerState CullFront { get; }

        public ID3D11RasterizerState LiftedLines { get; }

        public const int LineDepthBias = -6000;
        public const float LineSlopeBias = -2f;

        private readonly ID3D11RasterizerState meshNone;
        private readonly ID3D11RasterizerState meshBack;
        private readonly ID3D11RasterizerState meshFront;

        [field: ThreadStatic]
        public static bool IsMeshMode { get; set; }

        [field: ThreadStatic]
        public static bool IsMirrored { get; set; }

        public ID3D11RasterizerState Rasterizer(FaceCulling culling) => (IsMirrored ? Flipped(culling) : culling) switch
        {
            FaceCulling.Back => IsMeshMode ? meshBack : CullBack,
            FaceCulling.Front => IsMeshMode ? meshFront : CullFront,
            _ => IsMeshMode ? meshNone : CullNone,
        };

        private static FaceCulling Flipped(FaceCulling culling) => culling switch
        {
            FaceCulling.Back => FaceCulling.Front,
            FaceCulling.Front => FaceCulling.Back,
            _ => culling,
        };

        public ID3D11SamplerState LinearSampler { get; }

        public ID3D11SamplerState PointSampler { get; }

        public ID3D11SamplerState LinearWrapSampler { get; }

        public RenderStates(ID3D11Device device)
        {
            normal = CreateBlend(device, D3DBlend.SourceAlpha, D3DBlend.InverseSourceAlpha, BlendOperation.Add);
            add = CreateBlend(device, D3DBlend.SourceAlpha, D3DBlend.One, BlendOperation.Add);
            subtract = CreateBlend(device, D3DBlend.SourceAlpha, D3DBlend.One, BlendOperation.ReverseSubtract);
            multiply = CreateBlend(device, D3DBlend.DestinationColor, D3DBlend.InverseSourceAlpha, BlendOperation.Add);
            screen = CreateBlend(device, D3DBlend.One, D3DBlend.InverseSourceColor, BlendOperation.Add);
            accumulate = CreateBlend(device, D3DBlend.One, D3DBlend.One, BlendOperation.Add, D3DBlend.One, D3DBlend.One);

            NoColorWrite = disposer.CollectEx(device.CreateBlendState(new BlendDescription
            {
                RenderTarget =
                {
                    [0] = new RenderTargetBlendDescription { IsBlendEnabled = false, RenderTargetWriteMask = ColorWriteEnable.None },
                },
            }));

            DepthDefault = disposer.CollectEx(device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthFunc = ComparisonFunction.LessEqual,
            }));

            DepthDisabled = disposer.CollectEx(device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = false,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunction.Always,
            }));

            DepthTestOnly = disposer.CollectEx(device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunction.LessEqual,
            }));

            CullNone = CreateRasterizer(device, CullMode.None);
            CullBack = CreateRasterizer(device, CullMode.Back);
            CullFront = CreateRasterizer(device, CullMode.Front);
            LiftedLines = CreateRasterizer(device, CullMode.None, FillMode.Solid, LineDepthBias, LineSlopeBias);

            meshNone = CreateRasterizer(device, CullMode.None, FillMode.Wireframe);
            meshBack = CreateRasterizer(device, CullMode.Back, FillMode.Wireframe);
            meshFront = CreateRasterizer(device, CullMode.Front, FillMode.Wireframe);

            LinearSampler = CreateSampler(device, Filter.MinMagMipLinear);
            PointSampler = CreateSampler(device, Filter.MinMagMipPoint);
            LinearWrapSampler = CreateSampler(device, Filter.MinMagMipLinear, TextureAddressMode.Wrap);
        }

        public ID3D11BlendState NoColorWrite { get; }

        public ID3D11BlendState GetBlend(BlendMode mode) => mode switch
        {
            BlendMode.Add => add,
            BlendMode.Subtract => subtract,
            BlendMode.Multiply => multiply,
            BlendMode.Screen => screen,
            BlendMode.Accumulate => accumulate,
            _ => normal,
        };

        private ID3D11BlendState CreateBlend(
            ID3D11Device device,
            D3DBlend source,
            D3DBlend destination,
            BlendOperation operation,
            D3DBlend sourceAlpha = D3DBlend.One,
            D3DBlend destinationAlpha = D3DBlend.InverseSourceAlpha)
        {
            var desc = new BlendDescription();
            desc.RenderTarget[0] = new RenderTargetBlendDescription
            {
                IsBlendEnabled = true,
                RenderTargetWriteMask = ColorWriteEnable.All,
                SourceBlend = source,
                DestinationBlend = destination,
                BlendOperation = operation,
                SourceBlendAlpha = sourceAlpha,
                DestinationBlendAlpha = destinationAlpha,
                BlendOperationAlpha = BlendOperation.Add,
            };
            return disposer.CollectEx(device.CreateBlendState(desc));
        }

        private ID3D11RasterizerState CreateRasterizer(
            ID3D11Device device,
            CullMode cullMode,
            FillMode fillMode = FillMode.Solid,
            int depthBias = 0,
            float slopeBias = 0f)
        {
            return disposer.CollectEx(device.CreateRasterizerState(new RasterizerDescription
            {
                FillMode = fillMode,
                CullMode = cullMode,
                DepthBias = depthBias,
                SlopeScaledDepthBias = slopeBias,
                DepthClipEnable = true,
                MultisampleEnable = true,
                AntialiasedLineEnable = true,
                ScissorEnable = false,
            }));
        }

        private ID3D11SamplerState CreateSampler(ID3D11Device device, Filter filter, TextureAddressMode address = TextureAddressMode.Clamp)
        {
            return disposer.CollectEx(device.CreateSamplerState(new SamplerDescription
            {
                Filter = filter,
                AddressU = address,
                AddressV = address,
                AddressW = address,
                ComparisonFunction = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
            }));
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
