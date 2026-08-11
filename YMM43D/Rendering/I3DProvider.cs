using System.Numerics;
using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public interface I3DProvider
    {
        /// <summary>
        /// 描画の際に元の2D画像テクスチャが必要な場合は true を返します。
        /// </summary>
        bool RequiresMappedTexture { get; }

        void Draw(ID3D11Device device, ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext);
    }
}
