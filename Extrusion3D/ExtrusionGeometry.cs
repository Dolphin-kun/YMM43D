using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Rendering;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace Extrusion3D
{
    /// <summary>
    /// Extrusion3D エフェクト用のジオメトリ（立体化ボックス）。
    /// 前面 Z=0.0（元の2D平面）、背面 Z=1.0（奥方向）で構成される直方体。
    /// </summary>
    internal class ExtrusionGeometry : I3DGeometry
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer IndexBuffer { get; }
        public int IndexCount => 36;
        public InputElementDescription[] InputElements { get; }

        public ExtrusionGeometry(ID3D11Device device)
        {
            InputElements = [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 28, 0)
            ];

            var vertices = new[] {
                new Vertex(new(-0.5f,  0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(0f, 0f)), // 0: TL-Near
                new Vertex(new( 0.5f,  0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(1f, 0f)), // 1: TR-Near
                new Vertex(new(-0.5f, -0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(0f, 1f)), // 2: BL-Near
                new Vertex(new( 0.5f, -0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(1f, 1f)), // 3: BR-Near
                new Vertex(new(-0.5f,  0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(0f, 0f)), // 4: TL-Far
                new Vertex(new( 0.5f,  0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(1f, 0f)), // 5: TR-Far
                new Vertex(new(-0.5f, -0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(0f, 1f)), // 6: BL-Far
                new Vertex(new( 0.5f, -0.5f, 1.0f), new(1f, 1f, 1f, 1f), new(1f, 1f)), // 7: BR-Far
            };

            ushort[] indices = [
                // 前面 (z=0.0)
                0, 1, 2,  1, 3, 2,
                // 背面 (z=1.0)
                5, 4, 7,  4, 6, 7,
                // 左面 (x=-0.5)
                4, 0, 6,  0, 2, 6,
                // 右面 (x=0.5)
                1, 5, 3,  5, 7, 3,
                // 上面 (y=0.5)
                4, 5, 0,  5, 1, 0,
                // 下面 (y=-0.5)
                2, 3, 6,  3, 7, 6
            ];

            VertexBuffer = D3D11Helper.CreateBuffer(device, vertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);
            IndexBuffer = D3D11Helper.CreateBuffer(device, indices, BindFlags.IndexBuffer);
            disposer.Collect(IndexBuffer);
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
