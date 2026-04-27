using System.Collections.Concurrent;
using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public class DeviceResourceCache<T> : IDisposable where T : IDisposable
    {
        private readonly ConcurrentDictionary<nint, T> cache = new();
        private readonly Func<ID3D11Device, T> factory;

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
            SharedGraphics.RegisterForCleanup(this);
        }

        public T Get(ID3D11Device device)
        {
            return cache.GetOrAdd(device.NativePointer, _ => factory(device));
        }

        public void Dispose()
        {
            foreach (var res in cache.Values)
            {
                res.Dispose();
            }
            cache.Clear();

            GC.SuppressFinalize(this);
        }
    }
}
