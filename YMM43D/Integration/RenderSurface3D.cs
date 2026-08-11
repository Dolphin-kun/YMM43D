using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Integration
{
    /// <summary>
    /// 3D描画用のレンダーターゲットと深度バッファを持ち、その結果を
    /// YMM4 側のデバイスから <see cref="ID2D1Bitmap1"/> として参照できるようにします。
    /// </summary>
    /// <remarks>
    /// 3D描画は <see cref="GraphicsDevicePool"/> の独立デバイスで行い、YMM4 の描画は
    /// 本体のデバイスで行われます。異なるデバイス間で結果を受け渡すため、
    /// 共有リソースとして作成したテクスチャを本体側で開き直しています。
    /// </remarks>
    public sealed class RenderSurface3D : IDisposable
    {
        private DisposeCollector? disposer;
        private DeviceLease? lease;

        public ID3D11RenderTargetView? RenderTargetView { get; private set; }

        public ID3D11DepthStencilView? DepthStencilView { get; private set; }

        /// <summary>YMM4 のデバイスコンテキストで描画できる、3D描画結果のビットマップ。</summary>
        public ID2D1Bitmap1? Bitmap { get; private set; }

        /// <summary>現在確保しているサイズ。未確保なら (0, 0)。</summary>
        public (int Width, int Height) Size { get; private set; }

        /// <summary>
        /// 指定サイズで確保し直します。既に同じサイズなら何もしません。
        /// </summary>
        public void Resize(IGraphicsDevicesAndContext ymmDevices, int width, int height)
        {
            if (Size == (width, height) && RenderTargetView is not null)
                return;

            ReleaseResources();

            if (width <= 0 || height <= 0)
                return;

            var device = (lease ??= GraphicsDevicePool.Acquire()).Device;

            disposer = new DisposeCollector();

            var colorDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                // YMM4 側のデバイスから開けるように共有リソースにする。
                MiscFlags = ResourceOptionFlags.Shared,
            };

            ID3D11Texture2D renderTarget = Collect(device.CreateTexture2D(colorDesc));
            RenderTargetView = Collect(device.CreateRenderTargetView(renderTarget));

            var depthDesc = colorDesc with
            {
                Format = Format.D24_UNorm_S8_UInt,
                BindFlags = BindFlags.DepthStencil,
                MiscFlags = ResourceOptionFlags.None,
            };

            var depthBuffer = Collect(device.CreateTexture2D(depthDesc));
            DepthStencilView = Collect(device.CreateDepthStencilView(depthBuffer));

            using var dxgiResource = renderTarget.QueryInterface<IDXGIResource>();
            using var sharedTexture = ymmDevices.D3D.Device.OpenSharedResource<ID3D11Texture2D>(dxgiResource.SharedHandle);
            using var surface = sharedTexture.QueryInterface<IDXGISurface>();
            Bitmap = Collect(ymmDevices.DeviceContext.CreateBitmapFromDxgiSurface(surface));

            Size = (width, height);
        }

        private T Collect<T>(T resource) where T : IDisposable
        {
            disposer!.Collect(resource);
            return resource;
        }

        private void ReleaseResources()
        {
            disposer?.Dispose();
            disposer = null;

            Bitmap = null;
            DepthStencilView = null;
            RenderTargetView = null;
            Size = default;
        }

        public void Dispose()
        {
            ReleaseResources();
            lease?.Dispose();
            lease = null;
        }
    }
}
