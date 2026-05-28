using System.Numerics;
using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public interface I3DProvider
    {
        bool RequiresMappedTexture => false;

        void Draw(ID3D11Device device, ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext);
    }
}
