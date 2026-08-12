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
    /// 頂点カラーは白一色で、色はシェーダーが決めます。三角形2枚で1面、
    /// 前・後・左・右・上・下の順に並ぶので、面ごとに塗り分けたい場合は
    /// ピクセルシェーダーで <c>SV_PrimitiveID</c> を 2 で割ってください。
    /// <para>
    /// 面ごとに違う<b>画像</b>を貼りたい場合は、この形状ではなく
    /// <see cref="PlaneMesh"/> を面の数だけ描いてください。テクスチャは
    /// 描画1回につき1枚しか渡せません。
    /// </para>
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

        /// <summary>原点を中心とする 1×1×1 の立方体。</summary>
        public static BoxMesh CreateUnitCube(ID3D11Device device) => new(device, -0.5f, 0.5f);

        /// <summary>前面が Z=0（元の2D平面の位置）、背面が Z=1 の押し出しボックス。</summary>
        public static BoxMesh CreateExtrusionBox(ID3D11Device device) => new(device, 0f, 1f);

        private BoxMesh(ID3D11Device device, float zNear, float zFar)
        {
            var white = new Color4(1f, 1f, 1f, 1f);

            // 手前の面（zNear）が 0-3、奥の面（zFar）が 4-7。
            // 左上・右上・左下・右下の順に並ぶ。
            var vertices = new[]
            {
                new Vertex(new Vector3(-0.5f,  0.5f, zNear), white, new Vector2(0f, 0f)),
                new Vertex(new Vector3( 0.5f,  0.5f, zNear), white, new Vector2(1f, 0f)),
                new Vertex(new Vector3(-0.5f, -0.5f, zNear), white, new Vector2(0f, 1f)),
                new Vertex(new Vector3( 0.5f, -0.5f, zNear), white, new Vector2(1f, 1f)),
                new Vertex(new Vector3(-0.5f,  0.5f, zFar),  white, new Vector2(0f, 0f)),
                new Vertex(new Vector3( 0.5f,  0.5f, zFar),  white, new Vector2(1f, 0f)),
                new Vertex(new Vector3(-0.5f, -0.5f, zFar),  white, new Vector2(0f, 1f)),
                new Vertex(new Vector3( 0.5f, -0.5f, zFar),  white, new Vector2(1f, 1f)),
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
