using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    /// <summary>
    /// 頂点シェーダーとピクセルシェーダーの組。
    /// </summary>
    public interface IMaterial : IDisposable
    {
        ID3D11VertexShader VertexShader { get; }

        ID3D11PixelShader PixelShader { get; }

        /// <summary>
        /// 入力レイアウトの生成に必要な頂点シェーダーのバイトコード。
        /// </summary>
        byte[] VertexShaderBytecode { get; }
    }
}
