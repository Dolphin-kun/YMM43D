using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    /// <summary>
    /// 3D描画専用の D3D11 デバイスを、利用者間で共有しつつ参照カウントで管理します。
    /// </summary>
    /// <remarks>
    /// YMM4 本体のデバイスとは別に独立したデバイスを使います。本体のデバイスコンテキストは
    /// D2D の描画中に状態が変わるため、そこへ 3D のパイプラインステートを流し込むと
    /// 互いに干渉するためです。
    /// <para>
    /// 最後の利用者が解放した時点でデバイスと、それに紐づく
    /// <see cref="DeviceResourceCache{T}"/> のリソースをすべて破棄します。
    /// </para>
    /// </remarks>
    public static class GraphicsDevicePool
    {
        private static readonly Lock gate = new();
        private static readonly List<IDeviceResourceCache> caches = [];

        private static ID3D11Device? device;
        private static ID3D11DeviceContext? context;
        private static int refCount;

        /// <summary>
        /// デバイスを1つ借り受けます。返された <see cref="DeviceLease"/> を破棄すると解放されます。
        /// </summary>
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
            // キャッシュはデバイス由来のリソースを持っているため、
            // デバイス本体より先に解放する。
            foreach (var cache in caches.ToArray())
                cache.Clear();

            context?.Dispose();
            context = null;
            device?.Dispose();
            device = null;
        }
    }

    /// <summary>
    /// <see cref="GraphicsDevicePool"/> から借り受けたデバイス。
    /// 破棄すると参照カウントが1つ減ります。
    /// </summary>
    public readonly struct DeviceLease(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
    {
        public ID3D11Device Device { get; } = device;

        public ID3D11DeviceContext Context { get; } = context;

        public void Dispose() => GraphicsDevicePool.Release();
    }
}
