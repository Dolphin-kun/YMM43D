using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Extrusion3D
{
    [VideoEffect("立体化3D", ["3D"], ["立体化", "立体", "厚み", "押し出し", "extrude", "extrusion"])]
    public class Extrusion3DEffect : VideoEffect3DBase
    {
        private const string Group = "立体化3D";

        public override string Label => "立体化3D";

        [Display(GroupName = Group, Name = "厚み", Description = "奥へ押し出す厚み")]
        [AnimationSlider("F1", "px", 0, 100)]
        public Animation Thickness { get; } = new(10, 0, 1000);

        [Display(GroupName = Group, Name = "側面", Description = "側面の塗り方")]
        [EnumComboBox]
        public ExtrusionType ExtrusionType { get; set => Set(ref field, value, nameof(ExtrusionType), nameof(IsSolidSide)); } = ExtrusionType.Image;

        public bool IsSolidSide => ExtrusionType == ExtrusionType.Solid;

        [Display(GroupName = Group, Name = "色", Description = "側面を塗る色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSolidSide), true)]
        public Color SideColor { get => sideColor; set => Set(ref sideColor, value); }
        private Color sideColor = Colors.White;

        [Display(GroupName = Group, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit)); }

        [Display(GroupName = Group, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Group, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        internal SurfaceGloss GetGloss(in FrameContext time) => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new Extrusion3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Thickness, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
