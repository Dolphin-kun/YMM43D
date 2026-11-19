using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public sealed class MirrorModifier : ShapeModifier
    {
        public override string Title => "ミラー";

        public bool AxisX { get; set => Set(ref field, value); } = true;

        public bool AxisY { get; set => Set(ref field, value); }

        public bool AxisZ { get; set => Set(ref field, value); }

        public bool Bisects { get; set => Set(ref field, value); }

        public Animation Merge { get; } = new(1, 0, 100000);

        internal override ModifierStep? Step(in ModifierContext context)
            => AxisX || AxisY || AxisZ ? new MirrorStep(AxisX, AxisY, AxisZ, Bisects, Merge.GetFloat(context.Time) * context.UnitsPerPixel) : null;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Merge];
    }
}
