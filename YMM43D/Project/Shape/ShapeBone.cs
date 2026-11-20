using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    public sealed class ShapeBone : Animatable
    {
        public string Name { get; set => Set(ref field, value ?? string.Empty); } = "骨";

        public string Parent { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        public bool Deforms { get; set => Set(ref field, value); } = true;

        public double HeadX { get; set => Set(ref field, value); }

        public double HeadY { get; set => Set(ref field, value); }

        public double HeadZ { get; set => Set(ref field, value); }

        public double TailX { get; set => Set(ref field, value); }

        public double TailY { get; set => Set(ref field, value); } = -100;

        public double TailZ { get; set => Set(ref field, value); }

        internal Vector3 Head => WorldScale.ToWorldPosition((float)HeadX, (float)HeadY, (float)HeadZ);

        internal Vector3 Tail => WorldScale.ToWorldPosition((float)TailX, (float)TailY, (float)TailZ);

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
