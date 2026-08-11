using System;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace YMM43D.Commons
{
    internal class CachedItemTexture : IDisposable
    {
        public ID3D11Texture2D RenderTexture { get; }
        public ID3D11ShaderResourceView Srv { get; }
        public ID3D11Texture2D SharedTexture { get; }
        public ID2D1Bitmap1 TempBitmap { get; }
        public int Width { get; }
        public int Height { get; }

        public CachedItemTexture(ID3D11Device ourDevice, ID3D11Device ymmDevice, ID2D1DeviceContext d2dContext, int width, int height)
        {
            Width = width;
            Height = height;

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

            RenderTexture = ourDevice.CreateTexture2D(desc);
            Srv = ourDevice.CreateShaderResourceView(RenderTexture);

            using var dxgiResource = RenderTexture.QueryInterface<IDXGIResource>();
            nint sharedHandle = dxgiResource.SharedHandle;

            SharedTexture = ymmDevice.OpenSharedResource<ID3D11Texture2D>(sharedHandle);

            using var surface = SharedTexture.QueryInterface<IDXGISurface>();
            TempBitmap = d2dContext.CreateBitmapFromDxgiSurface(surface);
        }

        public void UpdateTexture(ID2D1DeviceContext d2dContext, ID2D1Image image, RawRectF bounds, ID3D11Device ymmDevice)
        {
            D3D11Helper.UpdateSharedTexture(d2dContext, image, TempBitmap, bounds, ymmDevice);
        }

        public void Dispose()
        {
            TempBitmap.Dispose();
            SharedTexture.Dispose();
            Srv.Dispose();
            RenderTexture.Dispose();
        }
    }
}
