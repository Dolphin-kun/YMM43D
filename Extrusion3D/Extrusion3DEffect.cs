using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Extrusion3D
{
    [VideoEffect("立体化3D", ["3D"], [])]
    public class Extrusion3DEffect : VideoEffect3DBase
    {
        public override string Label => "立体化3D";

        [Display(GroupName = "立体化3D", Name = "厚み", Description = "3D立体化の厚みを設定します")]
        [AnimationSlider("F1", "px", 0, 100)]
        public Animation Thickness { get; } = new(10, 0, 1000);

        [Display(GroupName = "立体化3D", Name = "側面", Description = "側面の種類")]
        [EnumComboBox]
        public ExtrusionType ExtrusionType
        {
            get => extrusionType;
            set
            {
                Set(ref extrusionType, value);
                OnPropertyChanged(nameof(IsSolidSide));
            }
        }
        private ExtrusionType extrusionType = ExtrusionType.Image;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsSolidSide => ExtrusionType == ExtrusionType.Solid;

        [Display(GroupName = "立体化3D", Name = "陰影をつけない",
            Description = "光源を無視して、元の色のまま塗ります")]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        [Display(GroupName = "立体化3D", Name = "色", Description = "側面の塗りつぶし色を設定します")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSolidSide), true)]
        public Color SideColor
        {
            get => sideColor;
            set => Set(ref sideColor, value);
        }
        private Color sideColor = Colors.White;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Extrusion3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Thickness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
