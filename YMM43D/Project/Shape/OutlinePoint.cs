using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace YMM43D.Project.Shape
{
    public class OutlinePoint : Animatable
    {
        private const string Group = "自由な形";
        private const double Limit = 100000;

        [Display(GroupName = Group, Name = "点", Description = "下書きの点の横の位置")]
        [AnimationSlider("F1", "px", -300, 300)]
        public Animation X { get; } = new(0, -Limit, Limit);

        [Display(GroupName = Group, Name = "", Description = "下書きの点の縦の位置")]
        [AnimationSlider("F1", "px", -300, 300)]
        public Animation Y { get; } = new(0, -Limit, Limit);

        public OutlinePoint()
        {
        }

        public OutlinePoint(double x, double y)
        {
            X.Values[0].Value = x;
            Y.Values[0].Value = y;
        }

        internal static OutlinePoint Between(OutlinePoint from, OutlinePoint to) => new((Start(from.X) + Start(to.X)) / 2, (Start(from.Y) + Start(to.Y)) / 2);

        internal static double DistanceSquared(OutlinePoint from, OutlinePoint to)
        {
            var alongX = Start(from.X) - Start(to.X);
            var alongY = Start(from.Y) - Start(to.Y);

            return alongX * alongX + alongY * alongY;
        }

        private static double Start(Animation animation) => animation.Values[0].Value;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [X, Y];
    }
}
