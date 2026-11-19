using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public abstract class ShapeModifier : Animatable
    {
        public bool IsEnabled { get; set => Set(ref field, value); } = true;

        public abstract string Title { get; }

        internal abstract ModifierStep? Step(in ModifierContext context);
    }
}
