using System.Numerics;
using System.Runtime.InteropServices;

namespace Noise3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NoiseConstants
    {
        public Vector4 Color;
        public Vector4 Box;
        public Vector4 Camera;
        public Vector4 Offset;
        public Vector4 Feature;
        public Vector4 Tone;
        public Vector4 Fractal;
        public Vector4 Warp;
    }
}
