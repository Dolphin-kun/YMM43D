using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    internal readonly record struct PointDeform(
        DeformKind Kind, Vector3 Axis, float Amount, float Period, float Phase)
    {
        public static PointDeform None => new(DeformKind.None, Vector3.UnitY, 0f, 1f, 0f);

        public static PointDeform Create(
            PixelPoints3DEffect effect, in FrameContext time, Vector3 extent)
        {
            var kind = effect.DeformKind;
            if (kind == DeformKind.None)
                return None;

            var axis = effect.DeformAxis switch
            {
                DeformAxis.X => Vector3.UnitX,
                DeformAxis.Z => Vector3.UnitZ,
                _ => Vector3.UnitY,
            };

            var strength = effect.DeformAmount.GetFloat(time) / 100f;

            var amount = kind switch
            {
                DeformKind.Twist => strength * MathF.PI,

                DeformKind.Sphere => Math.Clamp(strength, 0f, 1f),

                _ => strength * MathF.Min(extent.X, extent.Y) / 2f,
            };

            return new PointDeform(
                kind,
                axis,
                amount,
                MathF.Max(WorldScale.ToWorld(effect.DeformPeriod.GetFloat(time)), 1e-4f),
                Rotation3D.ToRadians(effect.DeformPhase.GetFloat(time)));
        }

        public Vector3 Expand(Vector3 half) => Kind switch
        {
            DeformKind.None => half,

            DeformKind.Wave => half + new Vector3(MathF.Abs(Amount)),

            DeformKind.Twist => Along(half) + Across(Vector3.One) * Across(half).Length(),

            DeformKind.Bulge => half + Axis * MathF.Abs(Amount),

            _ => Vector3.Max(half, new Vector3(SumOf(Across(half)))),
        };

        private Vector3 Along(Vector3 v) => Axis * Vector3.Dot(Axis, v);

        private Vector3 Across(Vector3 v) => v - Along(v);

        private static float SumOf(Vector3 v) => v.X + v.Y + v.Z;
    }
}
