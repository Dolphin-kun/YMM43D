using Vortice.DXGI;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Player
{
    public sealed class RenderSurface3D : IDisposable
    {
        private const ulong WriteKey = 0;

        private const ulong ReadKey = 1;

        private const int SyncTimeoutMs = 500;

        private const int SizeGranularity = 128;

        private const int RetiredGenerations = 2;

        private readonly List<DisposeCollector> retired = [];

        private DisposeCollector? disposer;
        private DeviceLease? lease;
        private IDXGIKeyedMutex? writeMutex;
        private IDXGIKeyedMutex? readMutex;
        private nint ymmDeviceKey;
        private bool holdsReadLock;
        private bool isBroken;

        public ID3D11RenderTargetView? RenderTargetView { get; private set; }

        public ID3D11DepthStencilView? DepthStencilView { get; private set; }

        public ID2D1Bitmap1? Bitmap { get; private set; }

        public (int Width, int Height) Size { get; private set; }

        public void Resize(IGraphicsDevicesAndContext ymmDevices, ID2D1DeviceContext deviceContext, int width, int height)
        {
            var ymmDevice = ymmDevices.D3D.Device;

            if (!isBroken
                && RenderTargetView is not null
                && ymmDeviceKey == ymmDevice.NativePointer
                && Fits(width, height))
            {
                return;
            }

            ReleaseResources();

            if (width <= 0 || height <= 0)
                return;

            width = Quantize(width);
            height = Quantize(height);

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
                MiscFlags = ResourceOptionFlags.SharedKeyedMutex,
            };

            ID3D11Texture2D renderTarget = Collect(device.CreateTexture2D(colorDesc));
            RenderTargetView = Collect(device.CreateRenderTargetView(renderTarget));
            writeMutex = Collect(renderTarget.QueryInterface<IDXGIKeyedMutex>());

            var depthDesc = colorDesc with
            {
                Format = Format.D24_UNorm_S8_UInt,
                BindFlags = BindFlags.DepthStencil,
                MiscFlags = ResourceOptionFlags.None,
            };

            var depthBuffer = Collect(device.CreateTexture2D(depthDesc));
            DepthStencilView = Collect(device.CreateDepthStencilView(depthBuffer));

            using var dxgiResource = renderTarget.QueryInterface<IDXGIResource>();
            var sharedTexture = Collect(ymmDevice.OpenSharedResource<ID3D11Texture2D>(dxgiResource.SharedHandle));
            readMutex = Collect(sharedTexture.QueryInterface<IDXGIKeyedMutex>());

            using var surface = sharedTexture.QueryInterface<IDXGISurface>();
            Bitmap = Collect(deviceContext.CreateBitmapFromDxgiSurface(surface));

            Size = (width, height);
            ymmDeviceKey = ymmDevice.NativePointer;
            isBroken = false;
        }

        private bool Fits(int width, int height)
            => Size.Width >= width
            && Size.Height >= height
            && Size.Width <= Quantize(width) * 2
            && Size.Height <= Quantize(height) * 2;

        private static int Quantize(int length)
            => (Math.Max(length, 1) + SizeGranularity - 1) / SizeGranularity * SizeGranularity;

        public bool BeginWrite()
        {
            ReleaseReadLock();

            if (writeMutex is null || isBroken)
                return false;

            return TryAcquire(writeMutex, WriteKey);
        }

        public void EndWrite()
        {
            if (writeMutex is null || isBroken)
                return;

            writeMutex.ReleaseSync(ReadKey);

            holdsReadLock = readMutex is not null && TryAcquire(readMutex, ReadKey);
        }

        private void ReleaseReadLock()
        {
            if (!holdsReadLock || readMutex is null)
                return;

            holdsReadLock = false;
            readMutex.ReleaseSync(WriteKey);
        }

        private bool TryAcquire(IDXGIKeyedMutex mutex, ulong key)
        {
            try
            {
                mutex.AcquireSync(key, SyncTimeoutMs);
                return true;
            }
            catch
            {
                isBroken = true;
                return false;
            }
        }

        private T Collect<T>(T resource) where T : IDisposable
        {
            disposer!.Collect(resource);
            return resource;
        }

        private void ReleaseResources()
        {
            if (!isBroken)
            {
                try { ReleaseReadLock(); } catch { }
            }

            holdsReadLock = false;
            writeMutex = null;
            readMutex = null;
            ymmDeviceKey = nint.Zero;

            if (disposer is not null)
                retired.Add(disposer);

            disposer = null;

            while (retired.Count > RetiredGenerations)
            {
                retired[0].Dispose();
                retired.RemoveAt(0);
            }

            Bitmap = null;
            DepthStencilView = null;
            RenderTargetView = null;
            Size = default;
        }

        public void Dispose()
        {
            ReleaseResources();

            foreach (var generation in retired)
                generation.Dispose();

            retired.Clear();

            lease?.Dispose();
            lease = null;
        }
    }
}
