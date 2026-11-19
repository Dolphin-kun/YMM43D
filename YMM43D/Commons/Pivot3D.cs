using System.Numerics;

namespace YMM43D.Commons
{
    public static class Pivot3D
    {
        public static Matrix4x4 Around(in Matrix4x4 orientation, in Vector3 pivot)
            => pivot.LengthSquared() < 1e-12f ? orientation : Matrix4x4.CreateTranslation(-pivot) * orientation * Matrix4x4.CreateTranslation(pivot);
    }
}
