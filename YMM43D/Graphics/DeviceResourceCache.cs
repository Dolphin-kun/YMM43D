using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public interface IDeviceResourceCache
    {
        void Clear();
    }

    public sealed class DeviceResourceCache<T> : IDeviceResourceCache, IDisposable where T : IDisposable
    {
        private readonly Lock gate = new();
        private readonly Dictionary<nint, T> cache = [];
        private readonly Func<ID3D11Device, T> factory;

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
            GraphicsDevicePool.RegisterCache(this);
        }

        public T Get(ID3D11Device device)
        {
            lock (gate)
            {
                if (cache.TryGetValue(device.NativePointer, out var found))
                    return found;

                return cache[device.NativePointer] = factory(device);
            }
        }

        public void Clear()
        {
            T[] values;

            lock (gate)
            {
                values = [.. cache.Values];
                cache.Clear();
            }

            foreach (var value in values)
                value.Dispose();
        }

        public void Dispose()
        {
            GraphicsDevicePool.UnregisterCache(this);
            Clear();
        }
    }
}
