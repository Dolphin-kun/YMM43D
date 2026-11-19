using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace PixelPoints3D
{
    public class FaceParams : Animatable
    {
        private const string Group = "面";

        [Display(GroupName = Group, Name = "不透明度", Description = "面だけに掛かる不透明度")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Opacity { get; } = new(100, 0, 100);

        [Display(GroupName = Group, Name = "不透明度のばらつき", Description = "面ごとに不透明度をランダムに散らす量")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation OpacityRandomness { get; } = new(0, 0, 100);

        [Display(GroupName = Group, Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        public PointColorSource ColorSource { get; set => Set(ref field, value, nameof(ColorSource), nameof(IsSolidColor)); } = PointColorSource.Image;

        public bool IsSolidColor => ColorSource == PointColorSource.Solid;

        [Display(GroupName = Group, Name = "色", Description = "面の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSolidColor), true)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Opacity, OpacityRandomness];
    }
}
