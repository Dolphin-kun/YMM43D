using System.Numerics;
using System.Runtime.InteropServices;

namespace Reflection3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ReflectionConstants
    {
        public const int PlaneSlots = 3;
        public const int CubeSlot = PlaneSlots;

        private const int MatrixFloats = 16;

        public Matrix4x4 NormalMatrix;
        public fixed float PlaneInverse[PlaneSlots * MatrixFloats];
        public fixed float FaceViewProjection[6 * MatrixFloats];
        public fixed float FaceInverse[6 * MatrixFloats];
        public Vector4 Viewport;
        public Vector4 Look;
        public Vector4 Options;
        public Vector4 Ripple;
        public Vector4 Tint;
        public Vector4 Cube;
        public Vector4 PlanarScale;
        public fixed float Slots[ReflectorShape.MaxPlanes];

        public void SetPlaneInverse(int slot, in Matrix4x4 matrix)
        {
            fixed (float* into = PlaneInverse)
                Write(into + slot * MatrixFloats, matrix);
        }

        public void SetFace(int face, in Matrix4x4 viewProjection, in Matrix4x4 inverse)
        {
            fixed (float* into = FaceViewProjection)
                Write(into + face * MatrixFloats, viewProjection);

            fixed (float* into = FaceInverse)
                Write(into + face * MatrixFloats, inverse);
        }

        public void SetSlot(int plane, int slot)
        {
            fixed (float* into = Slots)
                into[plane] = slot;
        }

        public void FillSlots(int slot)
        {
            for (var i = 0; i < ReflectorShape.MaxPlanes; i++)
                SetSlot(i, slot);
        }

        private static void Write(float* into, in Matrix4x4 matrix) => *(Matrix4x4*)into = Matrix4x4.Transpose(matrix);
    }
}
