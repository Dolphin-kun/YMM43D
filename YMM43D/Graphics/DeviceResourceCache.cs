using System.Collections.Concurrent;
using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public interface IDeviceResourceCache
    {
        void Clear();
    }

    public sealed class DeviceResourceCache<T> : IDeviceResourceCache, IDisposable where T : IDisposable
    {
        private readonly ConcurrentDictionary<nint, T> cache = new();
        private readonly Func<ID3D11Device, T> factory;

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
            GraphicsDevicePool.RegisterCache(this);
        }

        public T Get(ID3D11Device device)
        {
            return cache.GetOrAdd(device.NativePointer, _ => factory(device));
        }

        public void Clear()
        {
            foreach (var key in cache.Keys)
            {
                if (cache.TryRemove(key, out var value))
                    value.Dispose();
            }
        }

        public void Dispose()
        {
            GraphicsDevicePool.UnregisterCache(this);
            Clear();
        }
    }
}
