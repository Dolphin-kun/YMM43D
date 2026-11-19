using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace PixelPoints3D
{
    public class PointParams : Animatable
    {
        private const string Group = "点";

        [Display(GroupName = Group, Name = "大きさ", Description = "四角形なら一辺、円なら直径の長さ")]
        [AnimationSlider("F1", "px", 0, 50)]
        public Animation Size { get; } = new(4, 0, 500);

        [Display(GroupName = Group, Name = "形", Description = "粒の形")]
        [EnumComboBox]
        public PointShape Shape { get; set => Set(ref field, value); } = PointShape.Square;

        [Display(GroupName = Group, Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        public PointColorSource ColorSource { get; set => Set(ref field, value, nameof(ColorSource), nameof(IsSolidColor)); } = PointColorSource.Solid;

        public bool IsSolidColor => ColorSource == PointColorSource.Solid;

        [Display(GroupName = Group, Name = "色", Description = "粒の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSolidColor), true)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Size];
    }
}
