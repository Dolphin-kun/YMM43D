using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace YMM43D.Graphics.Models
{
    // 表情で頂点をずらす量を GPU に渡す入れ物。モデル 1 つにつき 1 つ持つ。
    // 動く頂点だけを書き換え、書き込みも動いた範囲だけに絞る。
    public sealed class ModelMorphBuffer(ID3D11Device device) : IDisposable
    {
        public const int Slot = 4;

        private readonly ID3D11Device device = device;

        private ID3D11Buffer? buffer;
        private ID3D11ShaderResourceView? view;
        private Vector4[] offsets = [];

        // 前に動かした頂点の範囲。次に書くときはここも元に戻す。
        private int touchedFrom = int.MaxValue;
        private int touchedTo = -1;

        public ID3D11ShaderResourceView? View => view;

        public ID3D11ShaderResourceView? Write(
            ID3D11DeviceContext context, ModelData model, IReadOnlyList<float>? weights)
        {
            if (!model.HasMorphs)
                return null;

            Reserve(model.Vertices.Length);

            if (buffer is null)
                return null;

            var from = int.MaxValue;
            var to = -1;

            // 前に動かしたところを元に戻す。
            for (var i = touchedFrom; i <= touchedTo; i++)
                offsets[i] = Vector4.Zero;

            if (weights is not null)
            {
                for (var m = 0; m < model.Morphs.Length && m < weights.Count; m++)
                {
                    var weight = weights[m];

                    if (weight is > -0.001f and < 0.001f)
                        continue;

                    foreach (var delta in model.Morphs[m].Deltas)
                    {
                        var at = (int)delta.Vertex;

                        if (at >= offsets.Length)
                            continue;

                        offsets[at] += new Vector4(delta.Offset * weight, 0f);

                        from = Math.Min(from, at);
                        to = Math.Max(to, at);
                    }
                }
            }

            var writeFrom = Math.Min(from, touchedFrom);
            var writeTo = Math.Max(to, touchedTo);

            touchedFrom = from;
            touchedTo = to;

            if (writeTo < writeFrom)
                return view;

            var stride = Marshal.SizeOf<Vector4>();

            context.UpdateSubresource(
                offsets.AsSpan(writeFrom, writeTo - writeFrom + 1),
                buffer,
                0,
                0,
                0,
                new Box(writeFrom * stride, 0, 0, (writeTo + 1) * stride, 1, 1));

            return view;
        }

        private void Reserve(int count)
        {
            if (buffer is not null && offsets.Length >= count)
                return;

            Release();

            var stride = Marshal.SizeOf<Vector4>();

            offsets = new Vector4[count];
            touchedFrom = int.MaxValue;
            touchedTo = -1;

            buffer = device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = stride,
                ByteWidth = stride * Math.Max(count, 1),
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
            offsets = [];
        }
    }
}
