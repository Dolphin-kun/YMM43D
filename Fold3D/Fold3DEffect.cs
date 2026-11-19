using System.ComponentModel.DataAnnotations;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Fold3D
{
    [VideoEffect("折る3D", ["3D"], ["折る", "折り目", "畳む", "蛇腹", "紙", "折り紙", "fold"])]
    public class Fold3DEffect : VideoEffect3DBase
    {
        private const string Group = "折る3D";

        public override string Label => "折る3D";

        [Display(GroupName = Group, Name = "折り込み", Description = "蛇腹に畳む角度")]
        [AnimationSlider("F1", "°", 0, 170)]
        public Animation Angle { get; } = new(0, 0, 179);

        [Display(GroupName = Group, Name = "折り目の数", Description = "何段に折るか")]
        [TextBoxSlider("F0", "", 2, 32)]
        [Range(1, 64)]
        public int Count { get; set => Set(ref field, Math.Clamp(value, 1, 64)); } = 6;

        [Display(GroupName = Group, Name = "軸の角度", Description = "どの向きに折るか")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation AxisAngle { get; } = new(0, -100000, 100000);

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

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new Fold3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Angle, AxisAngle, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
