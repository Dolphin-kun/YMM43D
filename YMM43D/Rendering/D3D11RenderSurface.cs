using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering
{
    public class D3D11RenderSurface : IDisposable
    {
        public ID3D11Texture2D? RenderTarget { get; private set; }
        public ID3D11RenderTargetView? RenderTargetView { get; private set; }
        public ID3D11Texture2D? DepthBuffer { get; private set; }
        public ID3D11DepthStencilView? DepthStencilView { get; private set; }
        public ID2D1Bitmap1? Bitmap { get; private set; }

        public D3D11RenderSurface()
        {
        }

        public void Recreate(IGraphicsDevicesAndContext devices, int width, int height)
        {
            Dispose();

            if (width <= 0 || height <= 0) return;

            var d3d3D = SharedGraphics.IndependentDevice;

            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.Shared
            };

            RenderTarget = d3d3D.CreateTexture2D(desc);
            RenderTargetView = d3d3D.CreateRenderTargetView(RenderTarget);

            var dsDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.D24_UNorm_S8_UInt,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil
            };
            DepthBuffer = d3d3D.CreateTexture2D(dsDesc);
            DepthStencilView = d3d3D.CreateDepthStencilView(DepthBuffer);

            using var dxgiResource = RenderTarget.QueryInterface<IDXGIResource>();
            var sharedHandle = dxgiResource.SharedHandle;
            using var sharedTexture = devices.D3D.Device.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
            using var surface = sharedTexture.QueryInterface<IDXGISurface>();
            Bitmap = devices.DeviceContext.CreateBitmapFromDxgiSurface(surface);
        }

        public void Dispose()
        {
            Bitmap?.Dispose();
            DepthStencilView?.Dispose();
            DepthBuffer?.Dispose();
            RenderTargetView?.Dispose();
            RenderTarget?.Dispose();

            Bitmap = null;
            DepthStencilView = null;
            DepthBuffer = null;
            RenderTargetView = null;
            RenderTarget = null;

            GC.SuppressFinalize(this);
        }
    }
}
