using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace YMM43D.Graphics
{
    /// <summary>
    /// 頂点バッファと（必要なら）インデックスバッファを持つ描画可能な形状。
    /// </summary>
    /// <remarks>
    /// インデックスを使わない形状（グリッドの TriangleStrip など）は
    /// <see cref="IndexBuffer"/> に <c>null</c> を返し、<see cref="DrawCount"/> に
    /// 頂点数を返してください。
    /// </remarks>
    public interface IMesh : IDisposable
    {
        ID3D11Buffer VertexBuffer { get; }

        /// <summary>インデックスバッファ。インデックス描画しない場合は <c>null</c>。</summary>
        ID3D11Buffer? IndexBuffer { get; }

        /// <summary>
        /// 描画する要素数。<see cref="IndexBuffer"/> がある場合はインデックス数、
        /// ない場合は頂点数です。
        /// </summary>
        int DrawCount { get; }

        /// <summary>頂点1個あたりのバイト数。</summary>
        int VertexStride { get; }

        /// <summary>この形状が要求する入力レイアウト。</summary>
        InputElementDescription[] InputElements { get; }

        /// <summary>プリミティブの種類。</summary>
        PrimitiveTopology Topology { get; }

        /// <summary>
        /// インデックスの型。
        /// </summary>
        /// <remarks>
        /// 既定は 16 ビットです。頂点が 65536 個を超える形状は
        /// <see cref="Format.R32_UInt"/> を返してください。
        /// </remarks>
        Format IndexFormat => Format.R16_UInt;
    }
}
