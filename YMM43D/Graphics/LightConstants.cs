using System.Numerics;
using System.Runtime.InteropServices;

namespace YMM43D.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LightConstants
    {
        public Vector4 Vector;

        public Vector4 Color;

        public Vector4 Cone;

        public Vector4 Edge;

        public Matrix4x4 Shadow;
    }
}
