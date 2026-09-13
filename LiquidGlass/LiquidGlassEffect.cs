using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace LiquidGlass
{
    public enum GlassShape
    {
        [Display(Name = "角丸四角")]
        RoundedRectangle,

        [Display(Name = "楕円")]
        Ellipse,
    }

    [VideoEffect("リキッドグラス", ["加工"], ["ガラス", "liquid glass", "すりガラス", "屈折", "レンズ"])]
    public sealed class LiquidGlassEffect : VideoEffectBase
    {
        private const string Form = "形";
        private const string Glass = "ガラス";
        private const string Light = "光と影";

        public override string Label => "リキッドグラス";

        [Display(GroupName = Form, Name = "形", Order = 100)]
        [EnumComboBox]
        public GlassShape Shape { get => shape; set => Set(ref shape, value); }
        private GlassShape shape = GlassShape.RoundedRectangle;

        [Display(GroupName = Form, Name = "X", Description = "ガラスの中心。アイテムの中心からのずれ", Order = 200)]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation X { get; } = new(0, -100000, 100000);

        [Display(GroupName = Form, Name = "Y", Description = "ガラスの中心。アイテムの中心からのずれ", Order = 300)]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Y { get; } = new(0, -100000, 100000);

        [Display(GroupName = Form, Name = "幅", Order = 400)]
        [AnimationSlider("F1", "px", 0, 1000)]
        public Animation Width { get; } = new(400, 0, 100000);

        [Display(GroupName = Form, Name = "高さ", Order = 500)]
        [AnimationSlider("F1", "px", 0, 1000)]
        public Animation Height { get; } = new(240, 0, 100000);

        [Display(GroupName = Form, Name = "角の丸み", Description = "角丸四角の角の半径。短い辺の半分で完全に丸くなります", Order = 600)]
        [AnimationSlider("F1", "px", 0, 200)]
        public Animation CornerRadius { get; } = new(60, 0, 100000);

        [Display(GroupName = Form, Name = "回転", Order = 700)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation Rotation { get; } = new(0, -100000, 100000);

        [Display(GroupName = Glass, Name = "屈折",
            Description = "ふちで背景をどれだけ曲げるか。マイナスにすると内側へ曲がります", Order = 100)]
        [AnimationSlider("F1", "px", -100, 100)]
        public Animation Refraction { get; } = new(40, -10000, 10000);

        [Display(GroupName = Glass, Name = "ふちの厚み", Description = "屈折と光が乗る、ふちの幅", Order = 200)]
        [AnimationSlider("F1", "px", 0, 200)]
        public Animation Bevel { get; } = new(40, 0, 10000);

        [Display(GroupName = Glass, Name = "ぼかし", Description = "ガラス越しの背景のぼけ具合。0 で透明なガラス", Order = 300)]
        [AnimationSlider("F1", "px", 0, 50)]
        public Animation Blur { get; } = new(6, 0, 1000);

        [Display(GroupName = Glass, Name = "色収差", Description = "ふちで色が分かれてにじむ強さ", Order = 400)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Dispersion { get; } = new(15, 0, 1000);

        [Display(GroupName = Glass, Name = "明るさ", Description = "ガラス越しの背景の明るさ。100 でそのまま", Order = 500)]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Brightness { get; } = new(105, 0, 1000);

        [Display(GroupName = Glass, Name = "色", Order = 600)]
        [ColorPicker]
        public Color TintColor { get => tintColor; set => Set(ref tintColor, value); }
        private Color tintColor = Colors.White;

        [Display(GroupName = Glass, Name = "色の濃さ", Description = "ガラスに色を重ねる強さ", Order = 700)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation TintAmount { get; } = new(8, 0, 100);

        [Display(GroupName = Glass, Name = "ガラスの外を表示", Description = "切ると、このアイテムの絵はガラスの部分だけが残ります。エフェクトアイテムでは、下のレイヤーはそのまま見えます", Order = 800)]
        [ToggleSlider]
        public bool ShowsOutside { get => showsOutside; set => Set(ref showsOutside, value); }
        private bool showsOutside = true;

        [Display(GroupName = Light, Name = "ふちの光", Description = "ふちに乗る照り返しの強さ", Order = 100)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Highlight { get; } = new(60, 0, 1000);

        [Display(GroupName = Light, Name = "光の向き", Description = "光が来る方向。0° で右、-90° で上から", Order = 200)]
        [AnimationSlider("F0", "°", -180, 180)]
        public Animation LightAngle { get; } = new(-135, -100000, 100000);

        [Display(GroupName = Light, Name = "影", Description = "ガラスのまわりに落ちる影の濃さ", Order = 300)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Shadow { get; } = new(25, 0, 100);

        [Display(GroupName = Light, Name = "影の広がり", Order = 400)]
        [AnimationSlider("F1", "px", 0, 100)]
        public Animation ShadowSpread { get; } = new(24, 0, 10000);

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => new LiquidGlassProcessor(devices, this);

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [X, Y, Width, Height, CornerRadius, Rotation, Refraction, Bevel, Blur, Dispersion,
                Brightness, TintAmount, Highlight, LightAngle, Shadow, ShadowSpread];
    }
}
