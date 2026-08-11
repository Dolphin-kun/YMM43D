using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Meshes
{
    /// <summary>
    /// XY 平面上の 1×1 の矩形を Z 方向に押し出した直方体。
    /// </summary>
    /// <remarks>
    /// 3D図形アイテムの立方体と、立体化エフェクトの押し出しボックスは
    /// Z の範囲と頂点カラーが違うだけで、頂点の並びも面の巻き方向も同一だったため
    /// このクラスに統合しています。
    /// </remarks>
    public sealed class BoxMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer { get; }
        public int DrawCount => 36;
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        /// <summary>
        /// 原点を中心とする 1×1×1 の立方体。面ごとの向きが分かるよう、
        /// 各頂点に異なる色が付きます。
        /// </summary>
        public static BoxMesh CreateUnitCube(ID3D11Device device) => new(device, -0.5f, 0.5f,
        [
            new(1f, 0f, 0f, 1f), new(0f, 1f, 0f, 1f), new(0f, 0f, 1f, 1f), new(1f, 1f, 0f, 1f),
            new(1f, 0f, 1f, 1f), new(0f, 1f, 1f, 1f), new(1f, 1f, 1f, 1f), new(0f, 0f, 0f, 1f),
        ]);

        /// <summary>
        /// 前面が Z=0（元の2D平面の位置）、背面が Z=1 の押し出しボックス。
        /// 頂点カラーは白一色で、色はテクスチャとシェーダーが決めます。
        /// </summary>
        public static BoxMesh CreateExtrusionBox(ID3D11Device device) => new(device, 0f, 1f, null);

        /// <param name="cornerColors">
        /// 8頂点の色。<c>null</c> の場合はすべて白になります。
        /// </param>
        private BoxMesh(ID3D11Device device, float zNear, float zFar, Color4[]? cornerColors)
        {
            var white = new Color4(1f, 1f, 1f, 1f);
            Color4 ColorAt(int i) => cornerColors is null ? white : cornerColors[i];

            // 手前の面（zNear）が 0-3、奥の面（zFar）が 4-7。
            // 左上・右上・左下・右下の順に並ぶ。
            var vertices = new[]
            {
                new Vertex(new Vector3(-0.5f,  0.5f, zNear), ColorAt(0), new Vector2(0f, 0f)),
                new Vertex(new Vector3( 0.5f,  0.5f, zNear), ColorAt(1), new Vector2(1f, 0f)),
                new Vertex(new Vector3(-0.5f, -0.5f, zNear), ColorAt(2), new Vector2(0f, 1f)),
                new Vertex(new Vector3( 0.5f, -0.5f, zNear), ColorAt(3), new Vector2(1f, 1f)),
                new Vertex(new Vector3(-0.5f,  0.5f, zFar),  ColorAt(4), new Vector2(0f, 0f)),
                new Vertex(new Vector3( 0.5f,  0.5f, zFar),  ColorAt(5), new Vector2(1f, 0f)),
                new Vertex(new Vector3(-0.5f, -0.5f, zFar),  ColorAt(6), new Vector2(0f, 1f)),
                new Vertex(new Vector3( 0.5f, -0.5f, zFar),  ColorAt(7), new Vector2(1f, 1f)),
            };

            // 全ての面が外側を向くよう、一貫した時計回り (CW) で定義する。
            ushort[] indices =
            [
                0, 1, 2,  1, 3, 2, // 前面 (zNear)
                5, 4, 7,  4, 6, 7, // 背面 (zFar)
                4, 0, 6,  0, 2, 6, // 左面 (x=-0.5)
                1, 5, 3,  5, 7, 3, // 右面 (x=+0.5)
                4, 5, 0,  5, 1, 0, // 上面 (y=+0.5)
                2, 3, 6,  3, 7, 6, // 下面 (y=-0.5)
            ];

            VertexBuffer = D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
            IndexBuffer = D3D11Buffers.Create(device, indices, BindFlags.IndexBuffer);
            disposer.Collect(IndexBuffer);
        }

        public void Dispose() => disposer.Dispose();
    }
}
