using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public sealed class SubdivisionModifier : ShapeModifier
    {
        public const int MaxLevels = 5;

        public override string Title => "細分化";

        public int Levels { get; set => Set(ref field, Math.Clamp(value, 0, MaxLevels)); } = 2;

        internal override ModifierStep? Step(in ModifierContext context) => Levels > 0 ? new SubdivisionStep(Levels) : null;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
