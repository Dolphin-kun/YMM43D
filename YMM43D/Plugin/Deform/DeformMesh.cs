using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Plugin
{
    public sealed class DeformMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }

        public ID3D11Buffer? IndexBuffer { get; }

        public int DrawCount { get; }

        public int VertexStride => DeformVertex.Stride;

        public InputElementDescription[] InputElements => DeformVertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public Format IndexFormat => Format.R32_UInt;

        public DeformGrid Grid { get; }

        public DeformMesh(ID3D11Device device, in DeformGrid grid)
        {
            Grid = grid;

            var (vertices, indices) = grid.Separated ? BuildPieces(grid) : BuildSheet(grid);

            VertexBuffer = disposer.CollectEx(D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer));
            IndexBuffer = disposer.CollectEx(D3D11Buffers.Create(device, indices, BindFlags.IndexBuffer));

            DrawCount = indices.Length;
        }

        private static (DeformVertex[] Vertices, uint[] Indices) BuildSheet(in DeformGrid grid)
        {
            var across = grid.X + 1;
            var down = grid.Y + 1;

            var vertices = new DeformVertex[across * down];

            for (int y = 0, v = 0; y < down; y++)
            {
                for (var x = 0; x < across; x++, v++)
                {
                    var uv = new Vector2((float)x / grid.X, (float)y / grid.Y);
                    var position = ToPlane(uv);

                    vertices[v] = new DeformVertex(position, uv, position);
                }
            }

            var indices = new uint[grid.X * grid.Y * 6];

            for (int y = 0, i = 0; y < grid.Y; y++)
            {
                for (var x = 0; x < grid.X; x++)
                {
                    var topLeft = (uint)(y * across + x);

                    i = AddQuad(indices, i, topLeft, topLeft + 1, topLeft + (uint)across, topLeft + (uint)across + 1);
                }
            }

            return (vertices, indices);
        }

        private static (DeformVertex[] Vertices, uint[] Indices) BuildPieces(in DeformGrid grid)
        {
            var quads = grid.X * grid.Y;

            var vertices = new DeformVertex[quads * 4];
            var indices = new uint[quads * 6];

            var v = 0;
            var i = 0;

            for (var y = 0; y < grid.Y; y++)
            {
                for (var x = 0; x < grid.X; x++)
                {
                    var left = (float)x / grid.X;
                    var right = (float)(x + 1) / grid.X;
                    var top = (float)y / grid.Y;
                    var bottom = (float)(y + 1) / grid.Y;

                    var piece = ToPlane(new Vector2((left + right) / 2f, (top + bottom) / 2f));
                    var start = (uint)v;

                    vertices[v++] = Corner(left, top, piece);
                    vertices[v++] = Corner(right, top, piece);
                    vertices[v++] = Corner(left, bottom, piece);
                    vertices[v++] = Corner(right, bottom, piece);

                    i = AddQuad(indices, i, start, start + 1, start + 2, start + 3);
                }
            }

            return (vertices, indices);
        }

        private static DeformVertex Corner(float u, float v, in Vector3 piece)
        {
            var uv = new Vector2(u, v);

            return new DeformVertex(ToPlane(uv), uv, piece);
        }

        private static Vector3 ToPlane(in Vector2 uv) => new(uv.X - 0.5f, 0.5f - uv.Y, 0f);

        private static int AddQuad(uint[] indices, int at, uint topLeft, uint topRight, uint bottomLeft, uint bottomRight)
        {
            indices[at++] = topLeft;
            indices[at++] = topRight;
            indices[at++] = bottomRight;
            indices[at++] = topLeft;
            indices[at++] = bottomRight;
            indices[at++] = bottomLeft;

            return at;
        }

        public void Dispose() => disposer.Dispose();
    }
}
