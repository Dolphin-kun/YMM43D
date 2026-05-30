using System;
using System.Collections.Concurrent;
using Vortice.Direct3D11;
using YMM43D.Rendering;

namespace YMM43D.Commons
{
    public class DeviceResourceCache<T> : IDisposable where T : IDisposable
    {
        private readonly ConcurrentDictionary<nint, T> cache = new();
        private readonly Func<ID3D11Device, T> factory;

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
            
            // 独立デバイスが破棄されるときに、現在キャッシュしているリソースを解放して辞書を空にする。
            // これにより、親オブジェクト（Shape3DParameter等）が生き残ったままデバイスが再作成された場合でも、
            // すでにDisposeされたキャッシュにアクセスしてクラッシュするのを防ぎ、かつ確実にリークを防止する。
            SharedGraphics.RegisterForCleanup(new CleanupAction(() =>
            {
                foreach (var val in cache.Values)
                {
                    val.Dispose();
                }
                cache.Clear();
            }));
        }

        public T Get(ID3D11Device device)
        {
            return cache.GetOrAdd(device.NativePointer, _ => factory(device));
        }

        public void Dispose()
        {
            foreach (var val in cache.Values)
            {
                val.Dispose();
            }
            cache.Clear();

            GC.SuppressFinalize(this);
        }
    }

    internal class CleanupAction : IDisposable
    {
        private readonly Action action;
        public CleanupAction(Action action)
        {
            this.action = action;
        }
        public void Dispose() => action();
    }
}
