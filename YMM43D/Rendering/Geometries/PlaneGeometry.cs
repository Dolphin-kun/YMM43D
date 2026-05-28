using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;
using YMM43D.Commons;

namespace YMM43D.Rendering.Geometries
{
    public class PlaneGeometry : I3DGeometry
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer IndexBuffer { get; }
        public int IndexCount => 6;
        public InputElementDescription[] InputElements { get; }

        public PlaneGeometry(ID3D11Device device)
        {
            InputElements = [
                new InputElementDescription("POSITION", 0, Vortice.DXGI.Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("COLOR", 0, Vortice.DXGI.Format.R32G32B32A32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Vortice.DXGI.Format.R32G32_Float, 28, 0)
            ];

            // 1x1 の平面（中心が 0,0）
            var vertices = new[] {
                new Vertex(new(-0.5f,  0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(0f, 0f)), // TL
                new Vertex(new( 0.5f,  0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(1f, 0f)), // TR
                new Vertex(new(-0.5f, -0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(0f, 1f)), // BL
                new Vertex(new( 0.5f, -0.5f, 0.0f), new(1f, 1f, 1f, 1f), new(1f, 1f)), // BR
            };

            ushort[] indices = [
                0, 1, 2,  1, 3, 2
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
