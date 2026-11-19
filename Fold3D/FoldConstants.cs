using System.Runtime.InteropServices;

namespace Fold3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FoldConstants
    {
        public float HalfAngle;
        public float Count;
        public float AxisRadians;
        public float Padding;
    }
}
