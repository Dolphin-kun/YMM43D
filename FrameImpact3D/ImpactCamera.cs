using System.Numerics;

namespace FrameImpact3D
{
    internal readonly record struct ImpactCamera(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Back)
    {
        public static ImpactCamera From(in Matrix4x4 view)
        {
            Matrix4x4.Invert(view, out var eye);

            return new ImpactCamera(eye.Translation, Axis(eye.M11, eye.M12, eye.M13, Vector3.UnitX), Axis(eye.M21, eye.M22, eye.M23, Vector3.UnitY), Axis(eye.M31, eye.M32, eye.M33, Vector3.UnitZ));
        }

        private static Vector3 Axis(float x, float y, float z, Vector3 fallback)
        {
            var axis = new Vector3(x, y, z);

            return axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : fallback;
        }
    }
}
