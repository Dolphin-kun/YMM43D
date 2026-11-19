using System.Runtime.InteropServices;

namespace Curve3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CurveConstants
    {
        public float BendRadians;
        public float TwistRadians;
        public float AxisRadians;
        public float Anchor;
    }
}
