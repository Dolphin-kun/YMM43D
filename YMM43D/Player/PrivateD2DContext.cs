using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Player
{
    public sealed class PrivateD2DContext : IDisposable
    {
        private nint deviceKey;
        private ID2D1DeviceContext6? context;

        public ID2D1DeviceContext6 For(IGraphicsDevicesAndContext ymmDevices)
        {
            var device = ymmDevices.D2D.Device;

            lock (D2DGate.Sync)
            {
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
