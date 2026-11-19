using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    public sealed class ShapeKey : Animatable
    {
        public string Name { get; set => Set(ref field, value ?? string.Empty); } = "キー";

        public Animation Value { get; } = new(0, -1000, 1000);

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Value];
    }
}
