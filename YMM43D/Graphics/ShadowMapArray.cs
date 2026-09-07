using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics
{
    // 光源から見た「いちばん手前にある物までの距離」を、光源ごとに1枚ずつ持つ板。
    // 影を落とせる光の数を絞っているのは、1灯ごとに場をもう一度描くため。
    public sealed class ShadowMapArray : IDisposable
    {
        public const int Slot = 2;

        public const int SamplerSlot = 1;

        public const int Size = 1024;

        public const int MaxSlices = 4;

        private static readonly DeviceResourceCache<ShadowMapArray> shared =
            new(device => new ShadowMapArray(device));

        private readonly DisposeCollector disposer = new();
        private readonly ID3D11DepthStencilView[] slices = new ID3D11DepthStencilView[MaxSlices];

        public static ShadowMapArray For(ID3D11Device device) => shared.Get(device);

        public static float Texel => 1f / Size;

        public ID3D11ShaderResourceView View { get; }

        public ID3D11SamplerState Sampler { get; }

        private ShadowMapArray(ID3D11Device device)
        {
            var texture = Collect(device.CreateTexture2D(new Texture2DDescription
            {
                Width = Size,
                Height = Size,
                MipLevels = 1,
                ArraySize = MaxSlices,
                Format = Format.R32_Typeless,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
            }));

            for (var i = 0; i < MaxSlices; i++)
            {
                slices[i] = Collect(device.CreateDepthStencilView(texture, new DepthStencilViewDescription(
                    texture, DepthStencilViewDimension.Texture2DArray, Format.D32_Float, 0, i, 1)));
            }

            View = Collect(device.CreateShaderResourceView(texture, new ShaderResourceViewDescription
            {
                Format = Format.R32_Float,
                ViewDimension = Vortice.Direct3D.ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayShaderResourceView
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    FirstArraySlice = 0,
                    ArraySize = MaxSlices,
                },
            }));

            // 板の外は「遮る物なし」として明るいままにしたいので、ふちの値は 1。
            Sampler = Collect(device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.ComparisonMinMagLinearMipPoint,
                AddressU = TextureAddressMode.Border,
                AddressV = TextureAddressMode.Border,
                AddressW = TextureAddressMode.Border,
                ComparisonFunction = ComparisonFunction.LessEqual,
                BorderColor = new Color4(1f, 1f, 1f, 1f),
                MinLOD = 0,
                MaxLOD = float.MaxValue,
            }));
        }

        public ID3D11DepthStencilView SliceAt(int index) => slices[index];

        public void Bind(ID3D11DeviceContext context)
        {
            context.PSSetShaderResource(Slot, View);
            context.PSSetSampler(SamplerSlot, Sampler);
        }

        public static void Unbind(ID3D11DeviceContext context)
            => context.PSSetShaderResource(Slot, null!);

        private T Collect<T>(T resource) where T : IDisposable
        {
            disposer.Collect(resource);
            return resource;
        }

        public void Dispose() => disposer.Dispose();
    }
}
