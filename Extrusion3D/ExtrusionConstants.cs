using System.Numerics;
using System.Runtime.InteropServices;

namespace Extrusion3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtrusionConstants
    {
        public Vector4 SideColor;
        public Vector3 CameraLocalPos;
        public int ExtrusionType;
    }
}
