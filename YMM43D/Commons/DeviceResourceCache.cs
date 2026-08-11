using System.Collections.Concurrent;
using Vortice.Direct3D11;
using YMM43D.Rendering;

namespace YMM43D.Commons
{
    public interface IDeviceResourceCache
    {
        void Clear();
    }

    public class DeviceResourceCache<T> : IDeviceResourceCache, IDisposable where T : IDisposable
    {
        private readonly ConcurrentDictionary<nint, T> cache = new();
        private readonly Func<ID3D11Device, T> factory;

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
            
            SharedGraphics.RegisterCache(this);
        }

        public T Get(ID3D11Device device)
        {
            return cache.GetOrAdd(device.NativePointer, _ => factory(device));
        }

        public void Clear()
        {
            foreach (var val in cache.Values)
            {
                val.Dispose();
            }
            cache.Clear();
        }

        public void Dispose()
        {
            Clear();
            GC.SuppressFinalize(this);
        }
    }
}
