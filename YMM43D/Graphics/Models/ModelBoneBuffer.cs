using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace YMM43D.Graphics.Models
{
    // 骨ごとの行列を GPU に渡す入れ物。モデル 1 つにつき 1 つ持つ。
    public sealed class ModelBoneBuffer(ID3D11Device device) : IDisposable
    {
        public const int Slot = 3;

        private const int MinimumCapacity = 16;

        private readonly ID3D11Device device = device;

        private ID3D11Buffer? buffer;
        private ID3D11ShaderResourceView? view;
        private Matrix4x4[] staging = [];

        public ID3D11ShaderResourceView? View => view;

        public void Write(ID3D11DeviceContext context, IReadOnlyList<Matrix4x4> matrices)
        {
            Reserve(matrices.Count);

            if (buffer is null)
                return;

            for (var i = 0; i < staging.Length; i++)
                staging[i] = i < matrices.Count ? matrices[i] : Matrix4x4.Identity;

            context.UpdateSubresource(staging.AsSpan(), buffer);
        }

        private void Reserve(int count)
        {
            var wanted = Math.Max(count, MinimumCapacity);

            if (buffer is not null && staging.Length >= wanted)
                return;

            var capacity = MinimumCapacity;

            while (capacity < wanted)
                capacity *= 2;

            Release();

            var stride = Marshal.SizeOf<Matrix4x4>();

            staging = new Matrix4x4[capacity];

            buffer = device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = stride,
                ByteWidth = stride * capacity,
            });

            view = device.CreateShaderResourceView(buffer);
        }

        private void Release()
        {
            view?.Dispose();
            view = null;
            buffer?.Dispose();
            buffer = null;
        }

        public void Dispose()
        {
            Release();
            staging = [];
        }
    }
}
