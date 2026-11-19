using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace FrameImpact3D
{
    internal sealed class ImpactCanvas : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public int Width { get; }

        public int Height { get; }

        public Format Format { get; }

        public ID3D11RenderTargetView Target { get; }

        public ID3D11ShaderResourceView View { get; }

        public ID3D11DepthStencilView Depth { get; }

        public ID3D11ShaderResourceView DepthView { get; }

        public ImpactCanvas(ID3D11Device device, int width, int height, Format format)
        {
            Width = width;
            Height = height;
            Format = format;

            var texture = disposer.CollectEx(device.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            }));

            Target = disposer.CollectEx(device.CreateRenderTargetView(texture));
            View = disposer.CollectEx(device.CreateShaderResourceView(texture));

            var depth = disposer.CollectEx(device.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R24G8_Typeless,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
            }));

            Depth = disposer.CollectEx(device.CreateDepthStencilView(depth, new DepthStencilViewDescription(depth, DepthStencilViewDimension.Texture2D, Format.D24_UNorm_S8_UInt)));
            DepthView = disposer.CollectEx(device.CreateShaderResourceView(depth, new ShaderResourceViewDescription(depth, ShaderResourceViewDimension.Texture2D, Format.R24_UNorm_X8_Typeless)));
        }

        public void Dispose() => disposer.Dispose();
    }
}
