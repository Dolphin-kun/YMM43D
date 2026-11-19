using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public sealed class ControlPoint3D : Animatable
    {
        private const double Limit = 1000000;

        public Animation X { get; } = new(0, -Limit, Limit);

        public Animation Y { get; } = new(0, -Limit, Limit);

        public Animation Z { get; } = new(0, -Limit, Limit);

        public ControlPoint3D()
        {
        }

        public ControlPoint3D(double x, double y, double z)
        {
            X.Values[0].Value = x;
            Y.Values[0].Value = y;
            Z.Values[0].Value = z;
        }

        public static ControlPoint3D Between(ControlPoint3D from, ControlPoint3D to)
            => new((Start(from.X) + Start(to.X)) / 2, (Start(from.Y) + Start(to.Y)) / 2, (Start(from.Z) + Start(to.Z)) / 2);

        public static ControlPoint3D After(ControlPoint3D from, double step) => new(Start(from.X) + step, Start(from.Y), Start(from.Z));

        private static double Start(Animation animation) => animation.Values[0].Value;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [X, Y, Z];
    }
}
