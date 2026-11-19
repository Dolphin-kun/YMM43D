using System.ComponentModel.DataAnnotations;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Curve3D
{
    [VideoEffect("湾曲3D", ["3D"], ["湾曲", "曲げ", "ねじり", "bend", "twist"])]
    public class Curve3DEffect : VideoEffect3DBase
    {
        private const string Group = "湾曲3D";

        public override string Label => "湾曲3D";

        [Display(GroupName = Group, Name = "曲げ角度", Description = "端から端までで回り込む角度")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation BendAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "ねじり角度", Description = "反対の端までにひねる角度")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation TwistAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "軸の角度", Description = "どの向きに沿って曲げるか")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation AxisAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "基準位置", Description = "曲げても動かない場所")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Anchor { get; } = new(50, -1000, 1000);

        [Display(GroupName = Group, Name = "分割の細かさ", Description = "板を何枚の面で作るか")]
        [TextBoxSlider("F0", "", 8, 128)]
        [Range(DeformGrid.MinSegments, DeformGrid.MaxSegments)]
        public int Segments { get; set => Set(ref field, Math.Clamp(value, DeformGrid.MinSegments, DeformGrid.MaxSegments)); } = 64;

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

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new Curve3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [BendAngle, TwistAngle, AxisAngle, Anchor, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
