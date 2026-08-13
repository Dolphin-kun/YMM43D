using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace YMM43D.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex(Vector3 position, Color4 color, Vector2 texCoord, Vector3 normal)
    {
        public Vector3 Position = position;
        public Color4 Color = color;
        public Vector2 TexCoord = texCoord;
        public Vector3 Normal = normal;

        public Vertex(Vector3 position, Color4 color, Vector2 texCoord)
            : this(position, color, texCoord, Vector3.Zero)
        {
        }

        public static int Stride => Marshal.SizeOf<Vertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 28, 0),
            new("NORMAL", 0, Format.R32G32B32_Float, 36, 0),
        ];

        public static Vector3 GetNormal(in Vector3 a, in Vector3 b, in Vector3 c)
        {
            var normal = Vector3.Cross(b - a, c - a);

            return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero;
        }
    }
}
