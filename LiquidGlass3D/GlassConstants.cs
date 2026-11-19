using System.Numerics;
using System.Runtime.InteropServices;

namespace LiquidGlass3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlassConstants
    {
        public Vector4 Size;
        public Vector4 Camera;
        public Vector4 Optics;
        public Vector4 Tint;
        public Vector4 Surface;
    }
}
