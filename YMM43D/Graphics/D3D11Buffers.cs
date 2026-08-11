using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    /// <summary>
    /// D3D11 バッファ生成のヘルパー。
    /// </summary>
    public static class D3D11Buffers
    {
        /// <summary>
        /// 定数バッファを生成します。サイズは D3D11 の要求どおり 16 バイト境界に切り上げられます。
        /// </summary>
        public static ID3D11Buffer CreateConstantBuffer<T>(ID3D11Device device) where T : unmanaged
        {
            return device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (Marshal.SizeOf<T>() + 15) / 16 * 16,
            });
        }

        /// <summary>
        /// 頂点バッファ・インデックスバッファなどを初期データ付きで生成します。
        /// </summary>
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
