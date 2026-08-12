using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;
using Color = System.Windows.Media.Color;

namespace Shape3D
{
    /// <summary>
    /// 正多面体の形状。面の色は頂点に焼き込みます。
    /// </summary>
    /// <remarks>
    /// 面ごとに描画を分けず、色を頂点に持たせて1回で描きます。二十面体でも
    /// 描画は1回で済み、シェーダーも頂点カラーを出すだけのもので足ります。
    /// 代わりに、色を変えると頂点バッファを作り直すことになります。色は
    /// キーフレームを持たないので、作り直すのは編集したときだけです。
    /// </remarks>
    internal sealed class PolyhedronMesh : IMesh
    {
        private readonly DisposeCollector disposer = new();

        public SolidKind Kind { get; }
        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer? IndexBuffer => null;
        public int DrawCount { get; }
        public int VertexStride => Vertex.Stride;
        public InputElementDescription[] InputElements => Vertex.InputElements;
        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public PolyhedronMesh(ID3D11Device device, SolidKind kind, IReadOnlyList<Color> faceColors)
        {
            Kind = kind;

            var solid = Polyhedron.Create(kind);
            var vertices = new List<Vertex>();

            for (var face = 0; face < solid.Faces.Length; face++)
            {
                var indices = solid.Faces[face];
                var color = ToColor4(faceColors, face);

                // 多角形は最初の頂点から扇状に割る。正多面体の面はどれも凸なので、
                // これで隙間も重なりも出ない。
                for (var i = 1; i + 1 < indices.Length; i++)
                {
                    vertices.Add(new Vertex(solid.Vertices[indices[0]], color, new Vector2(0.5f, 0f)));
                    vertices.Add(new Vertex(solid.Vertices[indices[i]], color, new Vector2(1f, 1f)));
                    vertices.Add(new Vertex(solid.Vertices[indices[i + 1]], color, new Vector2(0f, 1f)));
                }
            }

            DrawCount = vertices.Count;
            VertexBuffer = D3D11Buffers.Create(device, [.. vertices], BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
        }

        private static Color4 ToColor4(IReadOnlyList<Color> colors, int face)
        {
            if (colors.Count == 0)
                return new Color4(1f, 1f, 1f, 1f);

            // 面の数と色の数がずれていても破綻しないよう、足りなければ繰り返す。
            var color = colors[face % colors.Count];

            return new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }

        public void Dispose() => disposer.Dispose();
    }
}
