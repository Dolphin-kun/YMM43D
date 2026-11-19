using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace LiquidGlass3D
{
    [VideoEffect("リキッドグラス3D", ["3D"], ["リキッドグラス", "ガラス", "屈折", "透明", "レンズ", "水滴", "glass", "refraction"])]
    public class LiquidGlassEffect3D : VideoEffect3DBase, IGlassLook
    {
        private const string Form = "形";
        private const string Glass = "ガラス";
        private const string Surface = "表面";

        public override string Label => "リキッドグラス3D";

        [Display(GroupName = Form, Name = "形の細かさ", Description = "形を何マスの立体に写し取るか")]
        [TextBoxSlider("F0", "", DistanceFieldLayout.LeastResolution, DistanceFieldLayout.MostResolution)]
        [Range(DistanceFieldLayout.LeastResolution, DistanceFieldLayout.MostResolution)]
        public int Resolution { get; set => Set(ref field, Math.Clamp(value, DistanceFieldLayout.LeastResolution, DistanceFieldLayout.MostResolution)); } = 96;

        [Display(GroupName = Glass, Name = "屈折率", Description = "光の曲がり具合で、窓ガラスは約 1.5、水は約 1.33 です")]
        [AnimationSlider("F2", "", 1, 2)]
        public Animation RefractiveIndex { get; } = new(1.5, 1, 4);

        [Display(GroupName = Glass, Name = "向こうまでの距離", Description = "ガラスの向こうの物までのおおよその距離")]
        [AnimationSlider("F0", "px", 0, 1000)]
        public Animation Distance { get; } = new(300, 0, 100000);

        [Display(GroupName = Glass, Name = "すりガラス", Description = "ガラス越しの像のぼけ具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Frost { get; } = new(15, 0, 100);

        [Display(GroupName = Glass, Name = "色収差", Description = "像の色が分かれてにじむ強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Dispersion { get; } = new(15, 0, 1000);

        [Display(GroupName = Glass, Name = "色", Description = "ガラスの色")]
        [ColorPicker]
        public Color TintColor { get => tintColor; set => Set(ref tintColor, value); }
        private Color tintColor = Colors.White;

        [Display(GroupName = Glass, Name = "色の濃さ", Description = "ガラスの色を像に掛ける強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation TintAmount { get; } = new(10, 0, 100);

        [Display(GroupName = Surface, Name = "映り込み", Description = "斜めから見たふちほど強くなる、表面の照り返し")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Reflection { get; } = new(60, 0, 100);

        [Display(GroupName = Surface, Name = "つや", Description = "光源が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Gloss { get; } = new(60, 0, 1000);

        [Display(GroupName = Surface, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation GlossSharpness { get; } = new(80, 0, 100);

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new LiquidGlassEffect3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [RefractiveIndex, Distance, Frost, Dispersion, TintAmount, Reflection, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
