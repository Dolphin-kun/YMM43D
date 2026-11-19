using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    public sealed class ArrayModifier : ShapeModifier
    {
        public const int MaxCount = 1000;

        public override string Title => "配列";

        public int Count { get; set => Set(ref field, Math.Clamp(value, 1, MaxCount)); } = 3;

        public Animation RelativeX { get; } = new(100, -100000, 100000);

        public Animation RelativeY { get; } = new(0, -100000, 100000);

        public Animation RelativeZ { get; } = new(0, -100000, 100000);

        public Animation ConstantX { get; } = new(0, -100000, 100000);

        public Animation ConstantY { get; } = new(0, -100000, 100000);

        public Animation ConstantZ { get; } = new(0, -100000, 100000);

        public bool Merges { get; set => Set(ref field, value); }

        public Animation MergeDistance { get; } = new(1, 0, 100000);

        internal override ModifierStep? Step(in ModifierContext context)
        {
            if (Count <= 1)
                return null;

            var time = context.Time;
            var relative = new Vector3(RelativeX.GetFloat(time), -RelativeY.GetFloat(time), RelativeZ.GetFloat(time)) / 100f;
            var constant = new Vector3(ConstantX.GetFloat(time), -ConstantY.GetFloat(time), ConstantZ.GetFloat(time)) * context.UnitsPerPixel;

            return new ArrayStep(Count, relative, constant, Merges, MergeDistance.GetFloat(time) * context.UnitsPerPixel);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [RelativeX, RelativeY, RelativeZ, ConstantX, ConstantY, ConstantZ, MergeDistance];
    }
}
