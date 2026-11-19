using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace YMM43D.Plugin
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DeformVertex(Vector3 position, Vector2 texCoord, Vector3 piece)
    {
        public Vector3 Position = position;
        public Vector2 TexCoord = texCoord;
        public Vector3 Piece = piece;

        public static int Stride => Marshal.SizeOf<DeformVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
            new("PIECE", 0, Format.R32G32B32_Float, 20, 0),
        ];
    }
}
