using Vortice.Direct3D11;
using Vortice.Direct3D;

namespace YMM43D.Graphics
{
    public static class GraphicsDevicePool
    {
        private static readonly Lock gate = new();
        private static readonly List<IDeviceResourceCache> caches = [];

        private static ID3D11Device? device;
        private static ID3D11DeviceContext? context;
        private static int refCount;

        public static DeviceLease Acquire()
        {
            lock (gate)
            {
                EnsureDevice();
                refCount++;
                return new DeviceLease(device!, context!);
            }
        }

        internal static void Release()
        {
            lock (gate)
            {
                if (refCount > 0)
                    refCount--;
                if (refCount == 0)
                    DisposeDevice();
            }
        }

        internal static void RegisterCache(IDeviceResourceCache cache)
        {
            lock (gate)
            {
                caches.Add(cache);
            }
        }

        internal static void UnregisterCache(IDeviceResourceCache cache)
        {
            lock (gate)
            {
                caches.Remove(cache);
            }
        }

        private static void EnsureDevice()
        {
            if (device is not null)
                return;

            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                [],
                out device,
                out context).CheckError();
        }

        private static void DisposeDevice()
        {
            foreach (var cache in caches.ToArray())
                cache.Clear();

            context?.Dispose();
            context = null;
            device?.Dispose();
            device = null;
        }
    }

    public readonly struct DeviceLease(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
    {
        public ID3D11Device Device { get; } = device;

        public ID3D11DeviceContext Context { get; } = context;

        public void Dispose() => GraphicsDevicePool.Release();
    }
}
