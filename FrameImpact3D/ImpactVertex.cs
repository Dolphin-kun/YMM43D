using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FrameImpact3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ImpactVertex(Vector3 direction, Vector4 seed, Vector2 corner)
    {
        public Vector3 Direction = direction;
        public Vector4 Seed = seed;
        public Vector2 Corner = corner;

        public static int Stride => Marshal.SizeOf<ImpactVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("TEXCOORD", 0, Format.R32G32B32A32_Float, 12, 0),
            new("TEXCOORD", 1, Format.R32G32_Float, 28, 0),
        ];
    }
}
