using System.Numerics;
using System.Runtime.InteropServices;

namespace FrameImpact3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ImpactFrameConstants
    {
        public Vector4 Look;
        public Vector4 Spread;
        public Vector4 Focus;
        public Vector4 Tint;
        public Vector4 Range;
        public Vector4 Aim;
    }
}
