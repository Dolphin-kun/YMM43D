using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Fold3D
{
    internal sealed class Fold3DProcessor(Fold3DEffect effect, IGraphicsDevicesAndContext devices) : Deform3DProcessorBase<FoldConstants>(effect, devices)
    {
        private const int SegmentsPerFold = 4;

        private readonly Fold3DEffect effect = effect;

        protected override string ShaderName => "Fold.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override SurfaceGloss GetGloss(in FrameContext time) => effect.GetGloss(time);

        protected override DeformGrid GetGrid(in FrameContext time)
        {
            var along = effect.Count * SegmentsPerFold;

            return DeformGrid.Create(along, along);
        }

        protected override FoldConstants GetConstants(in FrameContext time) => new()
        {
            HalfAngle = float.DegreesToRadians(effect.Angle.GetFloat(time)) / 2f,
            Count = effect.Count,
            AxisRadians = float.DegreesToRadians(effect.AxisAngle.GetFloat(time)),
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            var rise = MathF.Sin(float.DegreesToRadians(effect.Angle.GetFloat(time)) / 2f) / MathF.Max(effect.Count, 1);

            return new DeformExtent(0f, rise);
        }
    }
}
