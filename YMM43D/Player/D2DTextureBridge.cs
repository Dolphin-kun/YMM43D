using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Player
{
    public sealed class D2DTextureBridge : IDisposable
    {
        private const long MaxTexturePixels = 4096L * 4096L;

        private readonly Lock gate = new();
        private readonly Dictionary<object, SharedItemTexture> cache = [];
        private readonly PrivateD2DContext privateContext = new();

        public ID3D11ShaderResourceView? GetTexture(ID3D11Device targetDevice, IGraphicsDevicesAndContext ymmDevices, ID2D1Image image, object key, out RawRectF bounds)
        {
            bounds = new RawRectF(0, 0, 1, 1);

            lock (D2DGate.Sync)
            {
                var deviceContext = privateContext.For(ymmDevices);
                if (deviceContext.NativePointer == nint.Zero)
                    return null;

                bounds = D2DImageBounds.Get(deviceContext, image);
                var (width, height) = D2DImageBounds.ToPixelSize(bounds);

                var scale = GetFittingScale(width, height);
                if (scale < 1f)
                {
                    width = Math.Max(1, (int)MathF.Ceiling(width * scale));
                    height = Math.Max(1, (int)MathF.Ceiling(height * scale));
                }

                return GetTextureCore(targetDevice, ymmDevices, deviceContext, image, key, bounds, width, height, scale);
            }
        }

        public void RetainOnly(IReadOnlySet<object> keys)
        {
            lock (gate)
            {
                foreach (var key in cache.Keys.Where(k => !keys.Contains(k)).ToArray())
                {
                    if (cache.Remove(key, out var texture))
                        texture.Dispose();
                }
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                foreach (var texture in cache.Values)
                    texture.Dispose();
                cache.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
            privateContext.Dispose();
        }

        private static float GetFittingScale(int width, int height)
        {
            var pixels = (long)width * height;

            return pixels <= MaxTexturePixels ? 1f : MathF.Sqrt((float)MaxTexturePixels / pixels);
        }

        private ID3D11ShaderResourceView? GetTextureCore(
            ID3D11Device targetDevice,
            IGraphicsDevicesAndContext ymmDevices,
            ID2D1DeviceContext deviceContext,
            ID2D1Image image,
            object key,
            in RawRectF bounds,
            int width,
            int height,
            float scale)
        {
            lock (gate)
            {
                if (cache.TryGetValue(key, out var texture) && !texture.Matches(width, height, ymmDevices.D3D.Device))
                {
                    texture.Dispose();
                    cache.Remove(key);
                    texture = null;
                }

                if (texture is null)
                {
                    try
                    {
                        texture = cache[key] = new SharedItemTexture(targetDevice, ymmDevices, deviceContext, width, height);
                    }
                    catch
                    {
                        cache.Remove(key);
                        return null;
                    }
                }

                texture.Update(ymmDevices, deviceContext, image, bounds, scale);
                return texture.ShaderResourceView;
            }
        }

        private sealed class SharedItemTexture : IDisposable
        {
            private readonly DisposeCollector disposer = new();
            private readonly int width;
            private readonly int height;
            private readonly nint ymmDevicePointer;
            private readonly ID2D1Bitmap1 targetBitmap;
            private readonly IDXGIKeyedMutex writeMutex;
            private readonly IDXGIKeyedMutex readMutex;

            private bool holdsReadLock;
            private bool isBroken;

            public ID3D11ShaderResourceView ShaderResourceView { get; }

            public SharedItemTexture(ID3D11Device targetDevice, IGraphicsDevicesAndContext ymmDevices, ID2D1DeviceContext deviceContext, int width, int height)
            {
                this.width = width;
                this.height = height;
                ymmDevicePointer = ymmDevices.D3D.Device.NativePointer;

                var texture = disposer.CollectEx(targetDevice.CreateTexture2D(new Texture2DDescription
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
                }));

                ShaderResourceView = disposer.CollectEx(targetDevice.CreateShaderResourceView(texture));
                readMutex = disposer.CollectEx(texture.QueryInterface<IDXGIKeyedMutex>());

                using var dxgiResource = texture.QueryInterface<IDXGIResource>();
                var shared = disposer.CollectEx(ymmDevices.D3D.Device.OpenSharedResource<ID3D11Texture2D>(dxgiResource.SharedHandle));
                writeMutex = disposer.CollectEx(shared.QueryInterface<IDXGIKeyedMutex>());

                using var surface = shared.QueryInterface<IDXGISurface>();
                targetBitmap = disposer.CollectEx(deviceContext.CreateBitmapFromDxgiSurface(surface));
            }

            public bool Matches(int width, int height, ID3D11Device ymmDevice)
                => !isBroken && this.width == width && this.height == height && ymmDevicePointer == ymmDevice.NativePointer;

            public void Update(IGraphicsDevicesAndContext ymmDevices, ID2D1DeviceContext deviceContext, ID2D1Image image, in RawRectF bounds, float scale)
            {
                ReleaseReadLock();

                if (isBroken || !TryAcquire(writeMutex, KeyedMutexes.WriteKey))
                    return;

                try
                {
                    lock (D2DGate.Sync)
                    {
                        var previousTarget = deviceContext.Target;
                        var previousTransform = deviceContext.Transform;

                        deviceContext.Target = targetBitmap;
                        deviceContext.BeginDraw();
                        deviceContext.Clear(null);
                        deviceContext.Transform = Matrix3x2.CreateTranslation(-bounds.Left, -bounds.Top) * Matrix3x2.CreateScale(scale);
                        deviceContext.DrawImage(image);
                        deviceContext.EndDraw();

                        deviceContext.Transform = previousTransform;
                        deviceContext.Target = previousTarget;

                        ymmDevices.D3D.Device.ImmediateContext.Flush();
                    }
                }
                finally
                {
                    writeMutex.ReleaseSync(KeyedMutexes.ReadKey);
                }

                holdsReadLock = TryAcquire(readMutex, KeyedMutexes.ReadKey);
            }

            public void Dispose()
            {
                if (!isBroken)
                {
                    try { ReleaseReadLock(); } catch { }
                }

                disposer.Dispose();
            }

            private void ReleaseReadLock()
            {
                if (!holdsReadLock)
                    return;

                holdsReadLock = false;
                readMutex.ReleaseSync(KeyedMutexes.WriteKey);
            }

            private bool TryAcquire(IDXGIKeyedMutex mutex, ulong key)
            {
                if (KeyedMutexes.TryAcquire(mutex, key))
                    return true;

                isBroken = true;
                return false;
            }
        }
    }
}
