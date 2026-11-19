using System.Numerics;
using System.Runtime.InteropServices;

namespace PixelPoints3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointCloudConstants
    {
        public Vector4 Color;
        public Vector3 GridCount;
        public float Threshold;
        public Vector3 Extent;
        public float Seed;
        public Vector3 Scatter;
        public float PointHalfSize;
        public Vector3 ViewRight;
        public float LineHalfWidth;
        public Vector3 ViewUp;
        public float UseSourceColor;
        public Vector3 ViewForward;
        public float ExtraOpacity;
        public Vector3 DeformAxis;
        public float DeformKind;
        public float DeformAmount;
        public float DeformPeriod;
        public float DeformPhase;
        public float LineRandomness;
        public float OpacityRandomness;
        public float PointIsRound;
        private readonly float padding0;
        private readonly float padding1;
    }
}
