using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Graphics;

namespace Noise3D
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NoiseVertex(Vector3 position)
    {
        public Vector3 Position = position;

        public static int Stride => Marshal.SizeOf<NoiseVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
        ];
    }

    public sealed class NoiseSliceMesh : IMesh
    {
        private const int VerticesPerSlice = 6;

        private readonly NoiseVertex[] vertices;
        private readonly int[] order;
        private readonly float[] distances;

        public ID3D11Buffer VertexBuffer { get; }

        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount => vertices.Length;

        public int VertexStride => NoiseVertex.Stride;

        public InputElementDescription[] InputElements => NoiseVertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public int SliceCount { get; }

        public NoiseSliceMesh(ID3D11Device device, int sliceCount)
        {
            SliceCount = sliceCount;
            vertices = new NoiseVertex[sliceCount * VerticesPerSlice];
            order = new int[sliceCount];
            distances = new float[sliceCount];

            VertexBuffer = device.CreateBuffer(new BufferDescription(
                vertices.Length * NoiseVertex.Stride, BindFlags.VertexBuffer, ResourceUsage.Default));
        }

        public static float SlicePosition(int index, int count) => -0.5f + (index + 0.5f) / count;

        public static int ChooseAxis(in Vector3 cameraLocal, in Vector3 boxPixels)
        {
            var away = -cameraLocal * boxPixels;
            var magnitude = Vector3.Abs(away);

            if (magnitude.X >= magnitude.Y && magnitude.X >= magnitude.Z)
                return 0;

            return magnitude.Y >= magnitude.Z ? 1 : 2;
        }

        public void Arrange(ID3D11DeviceContext context, int axis, float cameraCoordinate)
        {
            for (var i = 0; i < SliceCount; i++)
            {
                order[i] = i;
                distances[i] = -MathF.Abs(SlicePosition(i, SliceCount) - cameraCoordinate);
            }

            Array.Sort(distances, order);

            var v = 0;

            foreach (var index in order)
            {
                var t = SlicePosition(index, SliceCount);

                var a = Corner(axis, t, -0.5f, -0.5f);
                var b = Corner(axis, t, 0.5f, -0.5f);
                var c = Corner(axis, t, 0.5f, 0.5f);
                var d = Corner(axis, t, -0.5f, 0.5f);

                vertices[v++] = new NoiseVertex(a);
                vertices[v++] = new NoiseVertex(b);
                vertices[v++] = new NoiseVertex(c);
                vertices[v++] = new NoiseVertex(a);
                vertices[v++] = new NoiseVertex(c);
                vertices[v++] = new NoiseVertex(d);
            }

            context.UpdateSubresource(vertices, VertexBuffer);
        }

        private static Vector3 Corner(int axis, float along, float u, float w) => axis switch
        {
            0 => new Vector3(along, u, w),
            1 => new Vector3(u, along, w),
            _ => new Vector3(u, w, along),
        };

        public void Dispose() => VertexBuffer.Dispose();
    }
}
