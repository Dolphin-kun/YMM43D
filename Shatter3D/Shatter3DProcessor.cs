using System.Numerics;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Shatter3D
{
    internal sealed class Shatter3DProcessor(Shatter3DEffect effect, IGraphicsDevicesAndContext devices) : Deform3DProcessorBase<ShatterConstants>(effect, devices)
    {
        private const float FlyPerSecond = 1.2f;
        private const float FallPerSecond = 1.6f;
        private const float SpinPerSecond = 6f;
        private const float DelaySeconds = 0.6f;

        private readonly Shatter3DEffect effect = effect;

        protected override string ShaderName => "Shatter.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override SurfaceGloss GetGloss(in FrameContext time) => effect.GetGloss(time);

        protected override DeformGrid GetGrid(in FrameContext time)
        {
            if (!TryGetSize(out var size, out _) || size.X <= 0f || size.Y <= 0f)
                return DeformGrid.Create(16, 16, separated: true);

            var piece = (float)Math.Max(effect.Size, 1);

            return DeformGrid.Create((int)MathF.Ceiling(size.X / piece), (int)MathF.Ceiling(size.Y / piece), separated: true);
        }

        protected override ShatterConstants GetConstants(in FrameContext time) => new()
        {
            Seconds = ElapsedSeconds(time),
            FlySpeed = FlyPerSecond * Percent(effect.FlySpeed),
            FallSpeed = FallPerSecond * Percent(effect.FallSpeed),
            Delay = DelaySeconds * Percent(effect.Delay),
            ImpactPoint = ImpactPoint(time),
            Impact = Percent(effect.Impact),
            RandomRotate = Percent(effect.RandomRotate),
            RandomVector = Percent(effect.RandomVector),
            SpinRate = SpinPerSecond,
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            var seconds = ElapsedSeconds(time);

            if (seconds <= 0f)
                return new DeformExtent(0f, 0f);

            var flown = FlyPerSecond * Percent(effect.FlySpeed) * (1f + Percent(effect.Impact)) * seconds;
            var fallen = FallPerSecond * Percent(effect.FallSpeed) * seconds * seconds;
            var reach = MathF.Min(flown + fallen, 16f);

            return new DeformExtent(reach, MathF.Min(flown, 16f));
        }

        private float ElapsedSeconds(in FrameContext time)
        {
            var seconds = (float)time.Frame / Math.Max(time.Fps, 1);

            return (seconds - (float)effect.StartTime) * Percent(effect.PlaybackRate);
        }

        private Vector3 ImpactPoint(in FrameContext time)
        {
            if (!TryGetSize(out var size, out _) || size.X <= 0f || size.Y <= 0f)
                return Vector3.Zero;

            return new Vector3(effect.X.GetFloat(time) / size.X, -effect.Y.GetFloat(time) / size.Y, effect.Z.GetFloat(time) / size.X);
        }

        private static float Percent(double value) => (float)value / 100f;
    }
}
