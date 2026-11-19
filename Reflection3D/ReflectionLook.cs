using System.Numerics;
using YMM43D.Commons;

namespace Reflection3D
{
    internal readonly record struct ReflectionLook(
        float Strength,
        float Fresnel,
        float Blur,
        float Reach,
        Vector3 Tint,
        float RippleAmount,
        float RippleScale,
        float RipplePhase,
        float Quality)
    {
        private const float RippleReach = 0.03f;
        private const float RippleDensity = 0.2f;
        private const float RippleRate = 0.05f;

        public static ReflectionLook From(Reflection3DEffect effect, in FrameContext time)
        {
            var tint = effect.TintColor;
            var seconds = time.Fps > 0 ? (float)time.Frame / time.Fps : 0f;

            return new ReflectionLook(
                effect.Strength.GetFloat(time) / 100f,
                effect.Fresnel.GetFloat(time) / 100f,
                effect.Blur.GetFloat(time) / 100f,
                WorldScale.ToWorld(effect.Reach.GetFloat(time)),
                new Vector3(tint.R, tint.G, tint.B) / 255f,
                effect.RippleAmount.GetFloat(time) / 100f * RippleReach,
                effect.RippleScale.GetFloat(time) * RippleDensity,
                seconds * effect.RippleSpeed.GetFloat(time) * RippleRate,
                effect.Quality / 100f);
        }
    }
}
