using System.Numerics;
using System.Runtime.InteropServices;

namespace FrameImpact3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ImpactConstants
    {
        public Vector4 Center;
        public Vector4 AxisU;
        public Vector4 AxisV;
        public Vector4 AxisW;
        public Vector4 Right;
        public Vector4 Up;
        public Vector4 Tint;
        public Vector4 Aim;
    }
}
