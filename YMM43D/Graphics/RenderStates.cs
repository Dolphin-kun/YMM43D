using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;
using D3DBlend = Vortice.Direct3D11.Blend;

namespace YMM43D.Graphics
{
    /// <summary>
    /// 1つのデバイス上で共有される描画ステート一式。
    /// </summary>
    /// <remarks>
    /// 以前は BlendStates / DepthStencilStates / RasterizerStates / SamplerStates の
    /// 4クラスに分かれ、利用側がそれぞれ個別に生成・キャッシュしていました。
    /// ステートはデバイス単位で使い回せる不変オブジェクトなので、
    /// <see cref="DeviceResourceCache{T}"/> と組み合わせて1箇所で持ちます。
    /// </remarks>
    public sealed class RenderStates : IDisposable
    {
        private static readonly DeviceResourceCache<RenderStates> shared = new(device => new RenderStates(device));

        /// <summary>
        /// デバイスに対応する共有のステート一式を取得します。
        /// </summary>
        /// <remarks>
        /// ステートは不変なので、同じデバイスを使う描画すべてで1つを使い回せます。
        /// デバイスが破棄されるときに <see cref="GraphicsDevicePool"/> がまとめて解放します。
        /// </remarks>
        public static RenderStates For(ID3D11Device device) => shared.Get(device);

        private readonly DisposeCollector disposer = new();

        private readonly ID3D11BlendState normal;
        private readonly ID3D11BlendState noColorWrite;
        private readonly ID3D11BlendState add;
        private readonly ID3D11BlendState subtract;
        private readonly ID3D11BlendState multiply;
        private readonly ID3D11BlendState screen;

        /// <summary>深度テスト・深度書き込みを行う既定のステート。</summary>
        public ID3D11DepthStencilState DepthDefault { get; }

        /// <summary>深度を無視して常に描画するステート（最前面表示用）。</summary>
        public ID3D11DepthStencilState DepthDisabled { get; }

        /// <summary>深度テストは行うが、深度バッファには書き込まないステート。</summary>
        public ID3D11DepthStencilState DepthTestOnly { get; }

        /// <summary>カリングなし（両面を描画）。</summary>
        public ID3D11RasterizerState CullNone { get; }

        /// <summary>背面カリング（前面のみ描画）。</summary>
        public ID3D11RasterizerState CullBack { get; }

        /// <summary>前面カリング（背面のみ描画）。</summary>
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

            // 色を一切書かないステート。深度だけを埋めたいときに使う。
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

        /// <summary>色を書かず、深度だけを書き込むステート。</summary>
        public ID3D11BlendState NoColorWrite => noColorWrite;

        /// <summary>
        /// <see cref="BlendMode"/> に対応するブレンドステートを返します。
        /// </summary>
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
