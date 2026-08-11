using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace YMM43D.Graphics
{
    /// <summary>
    /// このライブラリの標準頂点フォーマット。
    /// 位置・頂点カラー・テクスチャ座標を持ちます。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex(Vector3 position, Color4 color, Vector2 texCoord)
    {
        public Vector3 Position = position;
        public Color4 Color = color;
        public Vector2 TexCoord = texCoord;

        /// <summary>1頂点あたりのバイト数。</summary>
        public static int Stride => Marshal.SizeOf<Vertex>();

        /// <summary>
        /// <see cref="Vertex"/> に対応する入力レイアウト記述。
        /// </summary>
        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 28, 0),
        ];
    }
}
