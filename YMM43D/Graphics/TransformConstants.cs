using System.Numerics;
using System.Runtime.InteropServices;

namespace YMM43D.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TransformConstants
    {
        public Matrix4x4 WorldViewProjection;

        public float Opacity;

        private Vector3 padding;

        public static TransformConstants Create(Matrix4x4 worldViewProjection, float opacity) => new()
        {
            WorldViewProjection = Matrix4x4.Transpose(worldViewProjection),
            Opacity = opacity,
        };
    }
}
