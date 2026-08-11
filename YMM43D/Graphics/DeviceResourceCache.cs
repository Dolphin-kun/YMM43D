using System.Collections.Concurrent;
using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    /// <summary>
    /// デバイス破棄時に一括で解放するために <see cref="GraphicsDevicePool"/> が
    /// 参照するキャッシュの共通操作。
    /// </summary>
    public interface IDeviceResourceCache
    {
        void Clear();
    }

    /// <summary>
    /// D3D11 リソースをデバイスごとに1つだけ生成して保持するキャッシュ。
    /// </summary>
    /// <remarks>
    /// プレビュー用の独立デバイスと YMM4 本体のデバイスの両方で同じリソースが
    /// 必要になるため、デバイスをキーにして使い分けます。
    /// キーにはデバイスのネイティブポインタを使いますが、デバイスが破棄される際は
    /// <see cref="GraphicsDevicePool"/> が全キャッシュを <see cref="Clear"/> するため、
    /// 解放済みアドレスの再利用によって誤ヒットすることはありません。
    /// </remarks>
    public sealed class DeviceResourceCache<T> : IDeviceResourceCache, IDisposable where T : IDisposable
    {
        private readonly ConcurrentDictionary<nint, T> cache = new();
        private readonly Func<ID3D11Device, T> factory;

        public DeviceResourceCache(Func<ID3D11Device, T> factory)
        {
            this.factory = factory;
            GraphicsDevicePool.RegisterCache(this);
        }

        /// <summary>
        /// 指定デバイス用のリソースを取得します。まだ無ければ生成します。
        /// </summary>
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
