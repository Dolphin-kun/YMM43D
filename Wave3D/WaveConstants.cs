using System.Runtime.InteropServices;

namespace Wave3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveConstants
    {
        public float Amplitude;
        public float Wavelength;
        public float PhaseRadians;
        public float AxisRadians;
        public int Ripple;
        public int Padding0;
        public int Padding1;
        public int Padding2;
    }
}
