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
    /// 格子の何番目の点かを表す頂点。
    /// </summary>
    /// <remarks>
    /// 座標は持ちません。実際の位置は頂点シェーダーが格子番号から組み立てます。
    /// <see cref="Corner"/> と <see cref="Other"/> の使い方は形状ごとに違います。
    /// <list type="table">
    /// <item><term>粒</term><description>四隅を <c>Corner</c> の ±1 で表す。<c>Other</c> は自分自身</description></item>
    /// <item><term>線</term><description><c>Other</c> がもう一方の端。<c>Corner.x</c> の ±1 で太さの左右に開く</description></item>
    /// <item><term>面</term><description>どちらも使わない</description></item>
    /// </list>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GridVertex(Vector3 cell, Vector2 corner, Vector3 other)
    {
        public Vector3 Cell = cell;
        public Vector2 Corner = corner;

        /// <summary>線のもう一方の端。線以外では自分自身と同じ値。</summary>
        public Vector3 Other = other;

        public GridVertex(Vector3 cell, Vector2 corner) : this(cell, corner, cell) { }

        public static int Stride => Marshal.SizeOf<GridVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("CELL", 0, Format.R32G32B32_Float, 0, 0),
            new("CORNER", 0, Format.R32G32_Float, 12, 0),
            new("OTHER", 0, Format.R32G32B32_Float, 20, 0),
        ];
    }

    /// <summary>格子の分割数。</summary>
    /// <remarks>各方向の「点の個数」であって、区画の数ではありません。</remarks>
    internal readonly record struct GridSize(int X, int Y, int Z)
    {
        public int PointCount => X * Y * Z;

        /// <summary>
        /// 大きさと間隔から分割数を求めます。上限を超える場合は間隔を粗くして収めます。
        /// </summary>
        /// <remarks>
        /// 切り捨てるのではなく粗くするのは、絵の範囲が変わらないほうが分かりやすいからです。
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
    /// 3つとも同じ格子番号を参照するので、作り直すのは分割数が変わったときだけです。
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

        /// <remarks>
        /// 線は <c>LineList</c> ではなく細長い四角形で作ります。D3D11 の線は太さを
        /// 持てず、常に 1 ピクセルになってしまうためです。
        /// <para>
        /// 右・下・奥に加えて、区画の対角線も引きます。面は四角形を三角形2枚に割って
        /// 張るので、対角線が無いと線と面で形が食い違います。
        /// </para>
        /// </remarks>
        private static IMesh? BuildLines(ID3D11Device device, GridSize size)
        {
            // 各点から右・下・奥へ1本ずつ。端の点は伸ばす先が無い。
            // 対角線は区画ごとに1本。
            var count = (size.X - 1) * size.Y * size.Z
                      + size.X * (size.Y - 1) * size.Z
                      + size.X * size.Y * (size.Z - 1)
                      + (size.X - 1) * (size.Y - 1) * size.Z;

            if (count <= 0)
                return null;

            var vertices = new GridVertex[count * 4];
            var indices = new uint[count * 6];

            var v = 0;
            var i = 0;

            void Connect(int x, int y, int z, int dx, int dy, int dz)
            {
                var from = new Vector3(x, y, z);
                var to = new Vector3(x + dx, y + dy, z + dz);
                var start = (uint)v;

                // 太さの向きは「相手へ向かう方向」から決まるので、相手側の2点は
                // 符号を入れ替えないと四角形がねじれる。
                vertices[v++] = new GridVertex(from, new Vector2(-1, 0), to);
                vertices[v++] = new GridVertex(from, new Vector2(1, 0), to);
                vertices[v++] = new GridVertex(to, new Vector2(1, 0), from);
                vertices[v++] = new GridVertex(to, new Vector2(-1, 0), from);

                indices[i++] = start;
                indices[i++] = start + 1;
                indices[i++] = start + 2;
                indices[i++] = start;
                indices[i++] = start + 2;
                indices[i++] = start + 3;
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

                        // 面の分割に合わせた対角線（左上 → 右下）。
                        if (x + 1 < size.X && y + 1 < size.Y) Connect(x, y, z, 1, 1, 0);
                    }
                }
            }

            return new GridMesh(device, vertices, indices, PrimitiveTopology.TriangleList);
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
