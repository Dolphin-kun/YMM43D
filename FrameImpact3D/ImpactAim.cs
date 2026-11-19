using System.Numerics;
using YMM43D.Commons;

namespace FrameImpact3D
{
    internal readonly record struct ImpactAim(Vector3 U, Vector3 V, Vector3 W, float Angle, float Spread)
    {
        public Vector3 Direction => Vector3.Normalize(MathF.Cos(Angle) * U + MathF.Sin(Angle) * V);

        public float Focus => Math.Clamp(1f - Spread / MathF.Tau, 0f, 1f);

        public static ImpactAim From(FrameImpact3DEffect effect, in FrameContext time, in ImpactAnchor anchor, in ImpactCamera camera)
        {
            var (u, v, w) = effect.Plane switch
            {
                ImpactPlane.Floor => (Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
                ImpactPlane.Item => (anchor.AxisX, -anchor.AxisY, Normal(anchor.AxisX, -anchor.AxisY)),
                _ => (camera.Right, -camera.Up, camera.Back),
            };

            return new ImpactAim(u, v, w, effect.GetAngle(time), effect.GetSpread(time));
        }

        private static Vector3 Normal(Vector3 u, Vector3 v)
        {
            var normal = Vector3.Cross(u, v);

            return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        }
    }
}
