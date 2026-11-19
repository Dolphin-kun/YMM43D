using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Shatter3D
{
    [VideoEffect("砕け散る3D", ["3D"], ["砕け散る", "クラッシュ", "割れ", "ガラス", "crash"])]
    public class Shatter3DEffect : VideoEffect3DBase
    {
        private const string Group = "砕け散る3D";
        private const string FromGroup = "どこから";
        private const string DetailGroup = "動きの詳細";

        public override string Label => "砕け散る3D";

        [Display(GroupName = Group, Name = "開始時間", Description = "アイテムの頭から何秒後に砕けるか")]
        [TextBoxSlider("F2", "秒", -10, 10)]
        [Range(-100000, 100000)]
        [DefaultValue(0d)]
        public double StartTime { get; set => Set(ref field, value); }

        [Display(GroupName = Group, Name = "再生速度", Description = "砕けが進む速さ")]
        [TextBoxSlider("F1", "%", -100, 100)]
        [Range(-100000, 100000)]
        [DefaultValue(100d)]
        public double PlaybackRate { get; set => Set(ref field, value); } = 100;

        [Display(GroupName = Group, Name = "破片の大きさ", Description = "1枚あたりの差し渡し")]
        [TextBoxSlider("F1", "px", 4, 200)]
        [Range(1, 100000)]
        [DefaultValue(50d)]
        public double Size { get; set => Set(ref field, Math.Max(1, value)); } = 50;

        [Display(GroupName = FromGroup, Name = "X", Description = "衝撃が来る横の位置")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation X { get; } = new(0, -100000, 100000);

        [Display(GroupName = FromGroup, Name = "Y", Description = "衝撃が来る縦の位置")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Y { get; } = new(0, -100000, 100000);

        [Display(GroupName = FromGroup, Name = "Z", Description = "衝撃が来る奥行きの位置で、手前が正です")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Z { get; } = new(0, -100000, 100000);

        [Display(GroupName = DetailGroup, Name = "飛ぶ速さ", Description = "破片が飛び散る速さ")]
        [TextBoxSlider("F1", "%", 0, 200)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double FlySpeed { get; set => Set(ref field, value); } = 100;

        [Display(GroupName = DetailGroup, Name = "落ちる速さ", Description = "破片が落ちる速さ")]
        [TextBoxSlider("F1", "%", 0, 200)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double FallSpeed { get; set => Set(ref field, value); } = 100;

        [Display(GroupName = DetailGroup, Name = "遅れ", Description = "破片ごとに飛び始めをずらす量")]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double Delay { get; set => Set(ref field, value); } = 100;

        [Display(GroupName = DetailGroup, Name = "衝撃", Description = "当たったところに近い破片ほど強く弾く量")]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double Impact { get; set => Set(ref field, value); } = 100;

        [Display(GroupName = DetailGroup, Name = "回転のばらつき", Description = "破片の回り方をばらつかせる量")]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double RandomRotate { get; set => Set(ref field, value); } = 100;

        [Display(GroupName = DetailGroup, Name = "方向のばらつき", Description = "破片の飛ぶ向きをばらつかせる量")]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double RandomVector { get; set => Set(ref field, value); } = 100;

        [Display(GroupName = DetailGroup, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit)); }

        [Display(GroupName = DetailGroup, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = DetailGroup, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        internal SurfaceGloss GetGloss(in FrameContext time) => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new Shatter3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [X, Y, Z, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
