using System;
using System.Collections.Concurrent;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;
using YMM43D.Rendering;

namespace YMM43D.Commons
{
    public class DeviceResourceCache<T> : IDisposable where T : IDisposable
    {
        private readonly ConcurrentDictionary<nint, T> cache = new();
        private readonly Func<ID3D11Device, T> factory;
        private readonly DisposeCollector disposer = new();

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
        }

        public T Get(ID3D11Device device)
        {
            return cache.GetOrAdd(device.NativePointer, _ => 
            {
                var res = factory(device);
                disposer.Collect(res);
                return res;
            });
        }

        public void Dispose()
        {
            disposer.Dispose();
            cache.Clear();

            GC.SuppressFinalize(this);
        }
    }
}
