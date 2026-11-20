using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public sealed class ArmatureModifier : ShapeModifier
    {
        public override string Title => "アーマチュア";

        internal override ModifierStep? Step(in ModifierContext context) => context.Skin is { } skin ? new ArmatureStep(skin) : null;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
