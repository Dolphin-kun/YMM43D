using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace YMM43D.Plugin
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DeformSolidVertex(Vector3 position, Color4 color, Vector3 normal, Vector3 piece)
    {
        public Vector3 Position = position;
        public Color4 Color = color;
        public Vector3 Normal = normal;
        public Vector3 Piece = piece;

        public static int Stride => Marshal.SizeOf<DeformSolidVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            new("NORMAL", 0, Format.R32G32B32_Float, 28, 0),
            new("PIECE", 0, Format.R32G32B32_Float, 40, 0),
        ];
    }
}
