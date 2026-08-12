using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    /// <summary>
    /// 格子の何番目の点か、粒の場合はその四隅のどれかを表す頂点。
    /// </summary>
    /// <remarks>
    /// 座標は持ちません。実際の位置は頂点シェーダーが格子番号から組み立てます。
    /// 間隔・ばらつき・奥行きを変えても、格子の数が同じならバッファを作り直さずに済みます。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GridVertex(Vector3 cell, Vector2 corner)
    {
        public Vector3 Cell = cell;
        public Vector2 Corner = corner;

        public static int Stride => Marshal.SizeOf<GridVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("CELL", 0, Format.R32G32B32_Float, 0, 0),
            new("CORNER", 0, Format.R32G32_Float, 12, 0),
        ];
    }

    /// <summary>格子の分割数。</summary>
    /// <remarks>各方向の「点の個数」であって、区画の数ではありません。</remarks>
    internal readonly record struct GridSize(int X, int Y, int Z)
    {
        public int PointCount => X * Y * Z;

        /// <summary>
        /// 大きさと間隔から分割数を求めます。
        /// </summary>
        /// <remarks>
        /// 点が多すぎると描画が止まってしまうため、上限を超える場合は間隔を
        /// 粗くして収めます。切り捨てるのではなく粗くするのは、絵の範囲が
        /// 変わらないほうが操作していて分かりやすいからです。
        /// </remarks>
        public static GridSize Create(Vector3 sizePixels, Vector3 spacingPixels, int maxPoints)
        {
            var scale = 1f;

            for (var i = 0; i < 16; i++)
            {
                var size = Divide(sizePixels, spacingPixels * scale);
                if (size.PointCount <= maxPoints)
                    return size;

                // 3方向に効くので、立方根ぶんだけ間隔を広げれば1回で収まる見当がつく。
                scale *= MathF.Max(1.05f, MathF.Cbrt((float)size.PointCount / maxPoints));
            }

            return new GridSize(1, 1, 1);
        }

        private static GridSize Divide(Vector3 size, Vector3 spacing) => new(
            CountAlong(size.X, spacing.X),
            CountAlong(size.Y, spacing.Y),
            CountAlong(size.Z, spacing.Z));

        private static int CountAlong(float size, float spacing)
        {
            if (!float.IsFinite(size) || !float.IsFinite(spacing) || spacing <= 0f || size <= 0f)
                return 1;

            // 両端に点を置くので、区画の数より1つ多い。
            return Math.Clamp((int)MathF.Floor(size / spacing) + 1, 1, 4096);
        }
    }

    /// <summary>
    /// 格子1つ分の、粒・線・面それぞれの形状。
    /// </summary>
    /// <remarks>
    /// 3つとも同じ格子番号を参照するので、間隔やばらつきを変えても作り直しは要りません。
    /// 分割数が変わったときだけ作り直します。
    /// </remarks>
    internal sealed class PointGrid : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public GridSize Size { get; }

        /// <summary>粒。1点につき四角形1枚。</summary>
        public IMesh Points { get; }

        /// <summary>隣り合う点をつなぐ線。</summary>
        public IMesh? Lines { get; }

        /// <summary>隣り合う4点を埋める三角形。</summary>
        public IMesh? Faces { get; }

        public PointGrid(ID3D11Device device, GridSize size)
        {
            Size = size;

            Points = Collect(BuildPoints(device, size));
            Lines = Collect(BuildLines(device, size));
            Faces = Collect(BuildFaces(device, size));
        }

        private static IMesh BuildPoints(ID3D11Device device, GridSize size)
        {
            var vertices = new GridVertex[size.PointCount * 4];
            var indices = new uint[size.PointCount * 6];

            var v = 0;
            var i = 0;

            for (var z = 0; z < size.Z; z++)
            {
                for (var y = 0; y < size.Y; y++)
                {
                    for (var x = 0; x < size.X; x++)
                    {
                        var cell = new Vector3(x, y, z);
                        var start = (uint)v;

                        vertices[v++] = new GridVertex(cell, new Vector2(-1, -1));
                        vertices[v++] = new GridVertex(cell, new Vector2(1, -1));
                        vertices[v++] = new GridVertex(cell, new Vector2(1, 1));
                        vertices[v++] = new GridVertex(cell, new Vector2(-1, 1));

                        indices[i++] = start;
                        indices[i++] = start + 1;
                        indices[i++] = start + 2;
                        indices[i++] = start;
                        indices[i++] = start + 2;
                        indices[i++] = start + 3;
                    }
                }
            }

            return new GridMesh(device, vertices, indices, PrimitiveTopology.TriangleList);
        }

        private static IMesh? BuildLines(ID3D11Device device, GridSize size)
        {
            // 各点から右・下・奥へ1本ずつ。端の点は伸ばす先が無い。
            var count = (size.X - 1) * size.Y * size.Z
                      + size.X * (size.Y - 1) * size.Z
                      + size.X * size.Y * (size.Z - 1);

            if (count <= 0)
                return null;

            var vertices = new GridVertex[count * 2];
            var v = 0;

            void Connect(int x, int y, int z, int dx, int dy, int dz)
            {
                vertices[v++] = new GridVertex(new Vector3(x, y, z), Vector2.Zero);
                vertices[v++] = new GridVertex(new Vector3(x + dx, y + dy, z + dz), Vector2.Zero);
            }

            for (var z = 0; z < size.Z; z++)
            {
                for (var y = 0; y < size.Y; y++)
                {
                    for (var x = 0; x < size.X; x++)
                    {
                        if (x + 1 < size.X) Connect(x, y, z, 1, 0, 0);
                        if (y + 1 < size.Y) Connect(x, y, z, 0, 1, 0);
                        if (z + 1 < size.Z) Connect(x, y, z, 0, 0, 1);
                    }
                }
            }

            return new GridMesh(device, vertices, null, PrimitiveTopology.LineList);
        }

        private static IMesh? BuildFaces(ID3D11Device device, GridSize size)
        {
            if (size.X < 2 || size.Y < 2)
                return null;

            var vertices = new GridVertex[size.PointCount];

            for (int z = 0, v = 0; z < size.Z; z++)
                for (var y = 0; y < size.Y; y++)
                    for (var x = 0; x < size.X; x++)
                        vertices[v++] = new GridVertex(new Vector3(x, y, z), Vector2.Zero);

            var quads = (size.X - 1) * (size.Y - 1) * size.Z;
            var indices = new uint[quads * 6];
            var i = 0;

            for (var z = 0; z < size.Z; z++)
            {
                var layer = (uint)(z * size.X * size.Y);

                for (var y = 0; y + 1 < size.Y; y++)
                {
                    for (var x = 0; x + 1 < size.X; x++)
                    {
                        var topLeft = layer + (uint)(y * size.X + x);
                        var topRight = topLeft + 1;
                        var bottomLeft = topLeft + (uint)size.X;
                        var bottomRight = bottomLeft + 1;

                        indices[i++] = topLeft;
                        indices[i++] = topRight;
                        indices[i++] = bottomRight;
                        indices[i++] = topLeft;
                        indices[i++] = bottomRight;
                        indices[i++] = bottomLeft;
                    }
                }
            }

            return new GridMesh(device, vertices, indices, PrimitiveTopology.TriangleList);
        }

        private T Collect<T>(T resource) where T : IDisposable?
        {
            if (resource is not null)
                disposer.Collect(resource);

            return resource;
        }

        public void Dispose() => disposer.Dispose();

        /// <summary>格子番号だけを持つ形状。</summary>
        private sealed class GridMesh : IMesh
        {
            private readonly DisposeCollector disposer = new();

            public ID3D11Buffer VertexBuffer { get; }
            public ID3D11Buffer? IndexBuffer { get; }
            public int DrawCount { get; }
            public int VertexStride => GridVertex.Stride;
            public InputElementDescription[] InputElements => GridVertex.InputElements;
            public PrimitiveTopology Topology { get; }

            /// <remarks>点の数は 65536 を軽く超えるため、インデックスは 32 ビット。</remarks>
            public Format IndexFormat => Format.R32_UInt;

            public GridMesh(
                ID3D11Device device,
                GridVertex[] vertices,
                uint[]? indices,
                PrimitiveTopology topology)
            {
                Topology = topology;

                VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
                disposer.Collect(VertexBuffer);

                if (indices is null)
                {
                    DrawCount = vertices.Length;
                    return;
                }

                IndexBuffer = D3D11Buffers.Create(device, indices, BindFlags.IndexBuffer);
                disposer.Collect(IndexBuffer);
                DrawCount = indices.Length;
            }

            public void Dispose() => disposer.Dispose();
        }
    }
}
