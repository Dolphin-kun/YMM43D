using System.ComponentModel.DataAnnotations;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Brush;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace Outline3D
{
    [VideoEffect("縁取り3D", ["3D"], ["縁取り", "ふちどり", "アウトライン", "ボーダー", "outline", "border"])]
    public sealed class Outline3DEffect : VideoEffect3DBase
    {
        private const string Outline = "縁取り3D";
        private const string Drawing = "描画";
        private const string Pattern = "縁の模様";

        public override string Label => "縁取り3D";

        [Display(GroupName = Outline, Name = "太さ", Description = "線の太さ")]
        [AnimationSlider("F1", "px", 0, 10)]
        public Animation StrokeThickness { get; } = new(3, 0, 500);

        [Display(GroupName = Outline, Name = "ぼかし", Description = "線のぼかし")]
        [AnimationSlider("F1", "px", 0, 5)]
        public Animation Blur { get; } = new(0, 0, 1000);

        [Display(GroupName = Outline, Name = "品質", Description = "縁の形を何角形で近づけるか")]
        [AnimationSlider("F0", "", 3, 64)]
        public Animation Quality { get; } = new(64, 3, 256);

        [Display(GroupName = Outline, Name = "なめらかさ", Description = "縁の外側のふちをなめらかにします")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Smoothness { get; } = new(100, 0, 100);

        [Display(GroupName = Outline, Name = "縁のみ", Description = "元の絵を描かず、縁だけを描きます")]
        [ToggleSlider]
        public bool IsOutlineOnly { get; set => Set(ref field, value); }

        [Display(GroupName = Outline, Name = "角縁取り", Description = "角ばった縁取りにします")]
        [ToggleSlider]
        public bool IsAngular { get; set => Set(ref field, value); }

        [Display(GroupName = Drawing, Name = "X", Description = "縁を横にずらす量")]
        [AnimationSlider("F1", "px", -50, 50)]
        public Animation X { get; } = new(0, -100000, 100000);

        [Display(GroupName = Drawing, Name = "Y", Description = "縁を縦にずらす量")]
        [AnimationSlider("F1", "px", -50, 50)]
        public Animation Y { get; } = new(0, -100000, 100000);

        [Display(GroupName = Drawing, Name = "不透明度", Description = "縁の不透明度")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Opacity { get; } = new(100, 0, 100);

        [Display(GroupName = Drawing, Name = "拡大率", Description = "縁の拡大率")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Zoom { get; } = new(100, 0, 100000);

        [Display(GroupName = Drawing, Name = "回転角", Description = "縁の回転角")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation Rotation { get; } = new(0, -100000, 100000);

        [Display(GroupName = Pattern, AutoGenerateField = true)]
        public Brush StrokeBrush { get; } = new();

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new Outline3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [StrokeThickness, Blur, Quality, Smoothness, X, Y, Opacity, Zoom, Rotation, StrokeBrush, CameraSyncAnimation];

        public override IEnumerable<string> GetFiles() => StrokeBrush.GetFiles();

        public override void ReplaceFile(string from, string to) => StrokeBrush.ReplaceFile(from, to);

        public override IEnumerable<TimelineResource> GetResources() => StrokeBrush.GetResources();

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
