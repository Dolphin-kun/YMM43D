using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering
{
    public static class SharedGraphics
    {
        public static IGraphicsDevicesAndContext? Devices { get; set; }

        private static ID3D11Device? independentDevice;
        private static ID3D11DeviceContext? independentContext;
        private static int independentRefCount;

        private static readonly Lock syncLock = new();
        private static DisposeCollector? disposer;
        private static readonly System.Collections.Generic.List<WeakReference<YMM43D.Commons.IDeviceResourceCache>> caches = new();

        public static void RegisterCache(YMM43D.Commons.IDeviceResourceCache cache)
        {
            lock (syncLock)
            {
                caches.RemoveAll(r => !r.TryGetTarget(out _));
                caches.Add(new WeakReference<YMM43D.Commons.IDeviceResourceCache>(cache));
            }
        }

        public static void RegisterForCleanup(IDisposable resource)
        {
            lock (syncLock)
            {
                disposer ??= new DisposeCollector();
                disposer.Collect(resource);
            }
        }

        public static ID3D11Device IndependentDevice
        {
            get
            {
                lock (syncLock)
                {
                    EnsureIndependentDevice();
                    return independentDevice!;
                }
            }
        }

        public static ID3D11DeviceContext IndependentContext
        {
            get
            {
                var _ = IndependentDevice;
                return independentContext!;
            }
        }

        public static void AcquireIndependentDevice(out ID3D11Device device, out ID3D11DeviceContext context)
        {
            lock (syncLock)
            {
                EnsureIndependentDevice();
                independentRefCount++;
                device = independentDevice!;
                context = independentContext!;
            }
        }

        public static void ReleaseIndependentDevice()
        {
            lock (syncLock)
            {
                if (independentRefCount > 0) independentRefCount--;
                if (independentRefCount == 0) DisposeIndependentDeviceCore();
            }
        }

        private static void EnsureIndependentDevice()
        {
            if (independentDevice != null) return;

            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                [],
                out independentDevice,
                out independentContext).CheckError();

            disposer ??= new DisposeCollector();
            disposer.Collect(independentDevice);
            disposer.Collect(independentContext);
        }

        private static void DisposeIndependentDeviceCore()
        {
            lock (syncLock)
            {
                foreach (var weakRef in caches)
                {
                    if (weakRef.TryGetTarget(out var cache))
                    {
                        cache.Clear();
                    }
                }
                caches.Clear();
            }

            disposer?.Dispose();
            disposer = null;
            independentContext = null;
            independentDevice = null;
        }

        public static void Dispose()
        {
            lock (syncLock)
            {
                independentRefCount = 0;
                DisposeIndependentDeviceCore();
            }
        }
    }
}
