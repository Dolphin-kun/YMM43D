using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public sealed class BevelModifier : ShapeModifier
    {
        public const int MaxSegments = 32;

        public override string Title => "ベベル";

        public Animation Amount { get; } = new(5, 0, 100000);

        public int Segments { get; set => Set(ref field, Math.Clamp(value, 1, MaxSegments)); } = 3;

        public double AngleLimit { get; set => Set(ref field, Math.Clamp(value, 0, 180)); } = 30;

        internal override ModifierStep? Step(in ModifierContext context)
        {
            var amount = Amount.GetFloat(context.Time) * context.UnitsPerPixel;

            return amount <= 0f ? null : new BevelStep(amount, Segments, float.DegreesToRadians((float)AngleLimit));
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Amount];
    }
}
