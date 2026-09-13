using System.Diagnostics.CodeAnalysis;
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
        private bool isDisposed;

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
            GraphicsDevicePool.RegisterCache(this);
        }

        public T Get(ID3D11Device device)
            => TryGet(device, out var value) ? value : throw new ObjectDisposedException(GetType().Name);

        public bool TryGet(ID3D11Device device, [MaybeNullWhen(false)] out T value)
        {
            lock (gate)
            {
                if (isDisposed)
                {
                    value = default;
                    return false;
                }

                if (!cache.TryGetValue(device.NativePointer, out value))
                    value = cache[device.NativePointer] = factory(device);

                return true;
            }
        }

        public void Clear() => Release(dispose: false);

        public void Dispose()
        {
            GraphicsDevicePool.UnregisterCache(this);
            Release(dispose: true);
        }

        private void Release(bool dispose)
        {
            T[] values;

            lock (gate)
            {
                isDisposed |= dispose;
                values = [.. cache.Values];
                cache.Clear();
            }

            foreach (var value in values)
                value.Dispose();
        }
    }
}
