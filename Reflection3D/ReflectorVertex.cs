using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Reflection3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ReflectorVertex(Vector3 position, Vector3 normal, float plane)
    {
        public Vector3 Position = position;
        public Vector3 Normal = normal;
        public float Plane = plane;

        public static int Stride => Marshal.SizeOf<ReflectorVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new("TEXCOORD", 0, Format.R32_Float, 24, 0),
        ];
    }
}
