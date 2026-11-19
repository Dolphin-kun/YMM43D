using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public readonly struct DeviceLease(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
    {
        public ID3D11Device Device { get; } = device;

        public ID3D11DeviceContext Context { get; } = context;

        public void Dispose() => GraphicsDevicePool.Release();
    }
}
