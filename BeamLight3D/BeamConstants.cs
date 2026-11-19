using System.Numerics;
using System.Runtime.InteropServices;

namespace BeamLight3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BeamConstants
    {
        public Vector4 Color;
        public Vector4 Shape;
    }
}
