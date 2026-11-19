using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public sealed class SolidifyModifier : ShapeModifier
    {
        public override string Title => "厚み";

        public Animation Thickness { get; } = new(10, -100000, 100000);

        internal override ModifierStep? Step(in ModifierContext context)
        {
            var thickness = Thickness.GetFloat(context.Time) * context.UnitsPerPixel;

            return thickness == 0f ? null : new SolidifyStep(thickness);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Thickness];
    }
}
