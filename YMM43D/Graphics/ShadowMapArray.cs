using System.Numerics;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics
{
    public sealed class ShadowMapArray : IDisposable
    {
        public const int Slot = 2;

        public const int SamplerSlot = 1;

        public const int DefaultSize = 1024;

        public const int MinSize = 256;

        public const int MaxSize = 4096;

        public const int MaxSlices = 24;

        private static readonly DeviceResourceCache<Holder> holders = new(device => new Holder(device));

        private readonly DisposeCollector disposer = new();
        private readonly ID3D11DepthStencilView[] slices;

        public int Size { get; }

        public int SliceCount => slices.Length;

        public float Texel => 1f / Size;

        public ID3D11ShaderResourceView View { get; }

        public ID3D11SamplerState Sampler { get; }

        public static ShadowMapArray For(ID3D11Device device, int size, int sliceCount)
            => holders.Get(device).Get(ClampSize(size), Math.Clamp(sliceCount, 1, MaxSlices));

        public static int ClampSize(int size)
            => (int)BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(size, MinSize, MaxSize));

        private ShadowMapArray(ID3D11Device device, int size, int sliceCount)
        {
            Size = size;
            slices = new ID3D11DepthStencilView[sliceCount];

            var texture = Collect(device.CreateTexture2D(new Texture2DDescription
            {
                Width = size,
                Height = size,
                MipLevels = 1,
                ArraySize = sliceCount,
                Format = Format.R32_Typeless,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
            }));

            for (var i = 0; i < sliceCount; i++)
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
                    ArraySize = sliceCount,
                },
            }));

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

        private sealed class Holder(ID3D11Device device) : IDisposable
        {
            private ShadowMapArray? current;

            public ShadowMapArray Get(int size, int sliceCount)
            {
                if (current is { } existing
                    && existing.Size == size
                    && existing.SliceCount >= sliceCount
                    && existing.SliceCount <= sliceCount * 2)
                {
                    return existing;
                }

                current?.Dispose();
                return current = new ShadowMapArray(device, size, sliceCount);
            }

            public void Dispose()
            {
                current?.Dispose();
                current = null;
            }
        }
    }
}
