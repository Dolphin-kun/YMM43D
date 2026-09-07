using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace BeamLight3D
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BeamVertex(Vector3 position, float weight)
    {
        public Vector3 Position = position;
        public float Weight = weight;

        public static int Stride => Marshal.SizeOf<BeamVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("TEXCOORD", 0, Format.R32_Float, 12, 0),
        ];
    }

    public readonly record struct BeamShape(int Rings, int Shells, int Blur)
    {
        public const int MinDetail = 8;

        public const int MaxDetail = 128;

        public static BeamShape Create(int detail, int blur)
        {
            var rings = Math.Clamp(detail, MinDetail, MaxDetail);

            return new BeamShape(rings, Math.Max(MinDetail, rings / 2), Math.Clamp(blur, 0, 100));
        }
    }

    // 先端が原点、+Z へ長さ 1、根元の半径 1 の円錐。
    // 太さの違う殻を内側から外側まで重ねてあり、足し込んで描くと
    // 通り抜けた殻の枚数がそのまま濃さになって、筋が中心ほど濃くなる。
    public sealed class BeamMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }

        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount { get; }

        public int VertexStride => BeamVertex.Stride;

        public InputElementDescription[] InputElements => BeamVertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public Format IndexFormat => Format.R32_UInt;

        public BeamShape Shape { get; }

        public BeamMesh(ID3D11Device device, in BeamShape shape)
        {
            Shape = shape;

            var vertices = Build(shape);

            VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);

            DrawCount = vertices.Length;
        }

        private static BeamVertex[] Build(in BeamShape shape)
        {
            var vertices = new BeamVertex[shape.Shells * shape.Rings * 3];
            var exponent = 0.5f + shape.Blur / 100f * 1.5f;

            var v = 0;

            for (var k = 1; k <= shape.Shells; k++)
            {
                var inner = (float)(k - 1) / shape.Shells;
                var outer = (float)k / shape.Shells;

                // 表と裏の 2 回通るので、1 枚あたりは半分にしておく。
                var weight = (Profile(inner, exponent) - Profile(outer, exponent)) / 2f;

                for (var i = 0; i < shape.Rings; i++)
                {
                    vertices[v++] = new BeamVertex(Vector3.Zero, weight);
                    vertices[v++] = new BeamVertex(OnRim(outer, i, shape.Rings), weight);
                    vertices[v++] = new BeamVertex(OnRim(outer, i + 1, shape.Rings), weight);
                }
            }

            return vertices;
        }

        // 中心から見た濃さの形。中心で 1、ふちで 0 になる。
        private static float Profile(float radius, float exponent)
            => MathF.Pow(MathF.Max(1f - radius * radius, 0f), exponent);

        private static Vector3 OnRim(float radius, int step, int count)
        {
            var angle = MathF.Tau * step / count;

            return new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 1f);
        }

        public void Dispose() => disposer.Dispose();
    }
}
