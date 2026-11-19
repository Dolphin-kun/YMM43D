using System.Numerics;
using System.Runtime.InteropServices;

namespace Shatter3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShatterConstants
    {
        public float Seconds;
        public float FlySpeed;
        public float FallSpeed;
        public float Delay;
        public Vector3 ImpactPoint;
        public float Impact;
        public float RandomRotate;
        public float RandomVector;
        public float SpinRate;
        public float Padding;
    }
}
