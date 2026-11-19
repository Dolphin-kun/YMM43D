using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace YMM43D.Project.Shape
{
    public class FaceColor : Animatable
    {
        private const string Group = "色";

        [Display(GroupName = Group, Name = "面", Description = "面の色で、上から面の並びと同じ順に並びます")]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
