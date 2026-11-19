using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace PixelPoints3D
{
    public class LineParams : Animatable
    {
        private const string Group = "線";

        [Display(GroupName = Group, Name = "太さ", Description = "つなぐ線の太さ")]
        [AnimationSlider("F1", "px", 0, 20)]
        public Animation Width { get; } = new(2, 0, 500);

        [Display(GroupName = Group, Name = "ランダム", Description = "線を引かない割合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Randomness { get; } = new(0, 0, 100);

        [Display(GroupName = Group, Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        public PointColorSource ColorSource { get; set => Set(ref field, value, nameof(ColorSource), nameof(IsSolidColor)); } = PointColorSource.Solid;

        public bool IsSolidColor => ColorSource == PointColorSource.Solid;

        [Display(GroupName = Group, Name = "色", Description = "線の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSolidColor), true)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Width, Randomness];
    }
}
