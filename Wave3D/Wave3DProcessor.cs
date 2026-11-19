using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Wave3D
{
    internal sealed class Wave3DProcessor(Wave3DEffect effect, IGraphicsDevicesAndContext devices) : Deform3DProcessorBase<WaveConstants>(effect, devices)
    {
        private readonly Wave3DEffect effect = effect;

        protected override string ShaderName => "Wave.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override SurfaceGloss GetGloss(in FrameContext time) => effect.GetGloss(time);

        protected override DeformGrid GetGrid(in FrameContext time) => DeformGrid.Create(effect.Segments, effect.Segments);

        protected override WaveConstants GetConstants(in FrameContext time) => new()
        {
            Amplitude = ToPlaneUnits(effect.Height.GetFloat(time)),
            Wavelength = ToPlaneUnits(effect.Wavelength.GetFloat(time)),
            PhaseRadians = float.DegreesToRadians(effect.Phase.GetFloat(time)),
            AxisRadians = float.DegreesToRadians(effect.AxisAngle.GetFloat(time)),
            Ripple = effect.IsRipple ? 1 : 0,
        };

        protected override DeformExtent GetExtent(in FrameContext time) => new(0f, MathF.Min(ToPlaneUnits(effect.Height.GetFloat(time)), 4f));

        private float ToPlaneUnits(float pixels) => TryGetSize(out var size, out _) && size.X > 0f ? pixels / size.X : 0f;
    }
}
