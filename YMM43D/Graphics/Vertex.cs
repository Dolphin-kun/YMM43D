using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace YMM43D.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex(Vector3 position, Color4 color, Vector2 texCoord)
    {
        public Vector3 Position = position;
        public Color4 Color = color;
        public Vector2 TexCoord = texCoord;

        public static int Stride => Marshal.SizeOf<Vertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 28, 0),
        ];
    }
}
