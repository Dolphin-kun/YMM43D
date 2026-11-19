using System.Numerics;
using System.Runtime.InteropServices;

namespace Backlight3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BacklightConstants
    {
        public Matrix4x4 BoxInverse;
        public Matrix4x4 Unproject;
        public Vector4 Tint;
        public Vector4 Air;
        public Vector4 Walk;
    }
}
