using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace YMM43D.Rendering.Geometries
{
    public class GridGeometry : IDisposable
    {
        public ID3D11Buffer VertexBuffer { get; }
        public int VertexCount { get; }

        public GridGeometry(ID3D11Device device)
        {
            // プロシージャルシェーダーで描画するため、色は白(1,1,1,1)を基本とする
            var white = new Color4(1.0f, 1.0f, 1.0f, 1.0f);
            float size = 1000f; 
            var vertices = new[]
            {
                new Vertex { Position = new Vector3(-size, 0,  size), Color = white, TexCoord = new Vector2(0, 0) },
                new Vertex { Position = new Vector3( size, 0,  size), Color = white, TexCoord = new Vector2(1, 0) },
                new Vertex { Position = new Vector3(-size, 0, -size), Color = white, TexCoord = new Vector2(0, 1) },
                new Vertex { Position = new Vector3( size, 0, -size), Color = white, TexCoord = new Vector2(1, 1) },
            };

            VertexCount = 4;
            VertexBuffer = D3D11Helper.CreateBuffer(device, vertices, BindFlags.VertexBuffer);
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
        }
    }
}
