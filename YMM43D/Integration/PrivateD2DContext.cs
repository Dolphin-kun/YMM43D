using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Integration
{
    /// <summary>
    /// プラグインが自分のためだけに使う Direct2D デバイスコンテキストを貸し出します。
    /// </summary>
    /// <remarks>
    /// <para>
    /// YMM4 の <see cref="IGraphicsDevicesAndContext.DeviceContext"/> は本体の描画スレッドが
    /// 使っています。描画先（<c>Target</c>）や描画中状態（<c>BeginDraw</c>〜<c>EndDraw</c>）は
    /// コンテキストが持つ状態なので、これを横から書き換えると、本体側の呼び出しが
    /// <c>D2DERR_WRONG_STATE</c>（0x88990001）で失敗します。ウィンドウの大きさを変えるなど、
    /// 本体の再描画が続けざまに走る場面で表面化します。
    /// </para>
    /// <para>
    /// 同じ Direct2D デバイスから別のコンテキストを作れば、描画状態は独立しつつ、
    /// 画像・ビットマップ・コマンドリストは本体との間でそのまま受け渡しできます。
    /// Direct2D の資源はコンテキストではなくデバイスに属するためです。
    /// </para>
    /// </remarks>
    public sealed class PrivateD2DContext : IDisposable
    {
        private nint deviceKey;
        private ID2D1DeviceContext6? context;

        /// <summary>
        /// 指定した YMM4 デバイスに対応する専用コンテキストを取得します。
        /// </summary>
        /// <remarks>
        /// 返されるコンテキストはこのオブジェクトが所有します。使う側は破棄せず、
        /// 触る間は <see cref="D2DGate.Sync"/> を保持してください。
        /// </remarks>
        public ID2D1DeviceContext6 For(IGraphicsDevicesAndContext ymmDevices)
        {
            var device = ymmDevices.D2D.Device;

            lock (D2DGate.Sync)
            {
                // デバイスが作り直された場合は、古いコンテキストは使えない。
                if (context is not null && deviceKey == device.NativePointer)
                    return context;

                context?.Dispose();
                context = device.CreateDeviceContext(DeviceContextOptions.None);
                deviceKey = device.NativePointer;
                return context;
            }
        }

        public void Dispose()
        {
            lock (D2DGate.Sync)
            {
                context?.Dispose();
                context = null;
                deviceKey = nint.Zero;
            }
        }
    }
}
