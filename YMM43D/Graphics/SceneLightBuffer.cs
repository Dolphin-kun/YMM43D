using System.Runtime.InteropServices;
using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public sealed class SceneLightBuffer : IDisposable
    {
        public const int Slot = 1;

        private const int MinimumCapacity = 4;

        private static readonly DeviceResourceCache<SceneLightBuffer> shared =
            new(device => new SceneLightBuffer(device));

        private readonly ID3D11Device device;

        private ID3D11Buffer? buffer;
        private ID3D11ShaderResourceView? view;
        private LightConstants[] staging = [];

        private SceneLightBuffer(ID3D11Device device)
        {
            this.device = device;
        }

        public static void Bind(
            ID3D11Device device, ID3D11DeviceContext context, IReadOnlyList<LightConstants> lights)
            => shared.Get(device).Write(context, lights);

        private void Write(ID3D11DeviceContext context, IReadOnlyList<LightConstants> lights)
        {
            Reserve(lights.Count);

            if (buffer is null || view is null)
                return;

            for (var i = 0; i < staging.Length; i++)
                staging[i] = i < lights.Count ? lights[i] : default;

            context.UpdateSubresource(staging.AsSpan(), buffer);
            context.VSSetShaderResource(Slot, view);
            context.PSSetShaderResource(Slot, view);
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

            var stride = Marshal.SizeOf<LightConstants>();

            staging = new LightConstants[capacity];

            buffer = device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = stride,
                ByteWidth = stride * capacity,
            });

            view = device.CreateShaderResourceView(buffer, new ShaderResourceViewDescription(
                ShaderResourceViewDimension.Buffer, Format.Unknown, 0, capacity));
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
