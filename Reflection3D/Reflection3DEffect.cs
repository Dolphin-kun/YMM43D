using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Reflection3D
{
    [VideoEffect("反射3D", ["3D"], ["反射", "映り込み", "鏡", "鏡面", "水面", "水たまり", "床", "テーブル", "つや", "reflection", "mirror", "water"])]
    public class Reflection3DEffect : VideoEffect3DBase
    {
        private const string Look = "映り込み";
        private const string Ripple = "揺らぎ";
        private const string Drawing = "描画";

        public const int LeastQuality = 25;
        public const int MostQuality = 100;

        public override string Label => "反射3D";

        [Display(GroupName = Look, Name = "強さ", Description = "映り込みの濃さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Strength { get; } = new(50, 0, 100);

        [Display(GroupName = Look, Name = "角度で変える", Description = "浅い角度から見るほど強く映り、真上から見るほど弱く映る度合い")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Fresnel { get; } = new(50, 0, 100);

        [Display(GroupName = Look, Name = "ぼかし", Description = "映り込みのぼけ具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Blur { get; } = new(0, 0, 100);

        [Display(GroupName = Look, Name = "届く距離", Description = "面からこの距離より離れたものは映らなくなり、0 なら離れても薄くしません")]
        [AnimationSlider("F0", "px", 0, 2000)]
        public Animation Reach { get; } = new(0, 0, 100000);

        [Display(GroupName = Look, Name = "色", Description = "映り込みに掛け合わせる色")]
        [ColorPicker]
        public Color TintColor { get; set => Set(ref field, value); } = Colors.White;

        [Display(GroupName = Ripple, Name = "強さ", Description = "水面のように映り込みを揺らす強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation RippleAmount { get; } = new(0, 0, 100);

        [Display(GroupName = Ripple, Name = "細かさ", Description = "揺らぎの波の細かさ")]
        [AnimationSlider("F0", "%", 1, 100)]
        public Animation RippleScale { get; } = new(30, 1, 1000);

        [Display(GroupName = Ripple, Name = "速さ", Description = "揺らぎが動く速さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation RippleSpeed { get; } = new(30, 0, 1000);

        [Display(GroupName = Drawing, Name = "映り込みの細かさ", Description = "映り込みを描く解像度で、下げると軽くなります")]
        [TextBoxSlider("F0", "%", LeastQuality, MostQuality)]
        [Range(LeastQuality, MostQuality)]
        public int Quality { get; set => Set(ref field, Math.Clamp(value, LeastQuality, MostQuality)); } = MostQuality;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new Reflection3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Strength, Fresnel, Blur, Reach, RippleAmount, RippleScale, RippleSpeed, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
