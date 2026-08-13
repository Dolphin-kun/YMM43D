using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public static class D3D11Buffers
    {
        public static ID3D11Buffer CreateConstantBuffer<T>(ID3D11Device device) where T : unmanaged
        {
            return device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (Marshal.SizeOf<T>() + 15) / 16 * 16,
            });
        }

        public static ID3D11Buffer Create<T>(ID3D11Device device, T[] data, BindFlags bindFlags) where T : unmanaged
        {
            if (data.Length == 0)
                throw new ArgumentException("空の配列からバッファは生成できません。", nameof(data));

            return device.CreateBuffer(data, new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = bindFlags,
                ByteWidth = Marshal.SizeOf<T>() * data.Length,
            });
        }
    }
}
