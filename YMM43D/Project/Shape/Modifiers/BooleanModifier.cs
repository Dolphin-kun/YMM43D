using YMM43D.Graphics.Meshes;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public sealed class BooleanModifier : ShapeModifier
    {
        public const int MaxLayer = 999;

        public override string Title => "ブーリアン";

        public BooleanOperation Operation { get; set => Set(ref field, value); }

        public int TargetLayer { get; set => Set(ref field, Math.Clamp(value, 0, MaxLayer)); } = 1;

        internal override ModifierStep? Step(in ModifierContext context)
            => context.Targets?.Invoke(TargetLayer) is { } target ? new BooleanStep(Operation, target.Mesh, target.ToLocal) : null;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
