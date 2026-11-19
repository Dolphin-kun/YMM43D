using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Curve3D
{
    internal sealed class Curve3DProcessor(Curve3DEffect effect, IGraphicsDevicesAndContext devices) : Deform3DProcessorBase<CurveConstants>(effect, devices)
    {
        private readonly Curve3DEffect effect = effect;

        protected override string ShaderName => "Curve.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override SurfaceGloss GetGloss(in FrameContext time) => effect.GetGloss(time);

        protected override DeformGrid GetGrid(in FrameContext time) => DeformGrid.Create(effect.Segments, effect.Segments);

        protected override CurveConstants GetConstants(in FrameContext time) => new()
        {
            BendRadians = float.DegreesToRadians(effect.BendAngle.GetFloat(time)),
            TwistRadians = float.DegreesToRadians(effect.TwistAngle.GetFloat(time)),
            AxisRadians = float.DegreesToRadians(effect.AxisAngle.GetFloat(time)),
            Anchor = effect.Anchor.GetFloat(time) / 100f - 0.5f,
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            var bend = MathF.Abs(float.DegreesToRadians(effect.BendAngle.GetFloat(time)));
            var reach = bend > 1e-4f ? MathF.Min(2f / bend, 1.5f) : 0.75f;

            return new DeformExtent(reach, reach);
        }
    }
}
