using System.ComponentModel.DataAnnotations;
using System.Numerics;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace RandomScatter3D
{
    [VideoEffect("ランダム配置3D", ["3D"], ["ランダム配置", "複製", "ばらまく", "雪", "紙吹雪", "scatter"])]
    public sealed class RandomScatter3DEffect : VideoEffect3DBase
    {
        public const int MaxCount = 2000;

        private const string Placement = "ランダム配置3D";
        private const string Movement = "ランダム移動";
        private const string Look = "見た目";

        public override string Label => "ランダム配置3D";

        [Display(GroupName = Placement, Name = "配置数", Description = "配置する数")]
        [TextBoxSlider("F0", "個", 1, 200)]
        [Range(1, MaxCount)]
        public int Count { get; set => Set(ref field, Math.Clamp(value, 1, MaxCount)); } = 30;

        [Display(GroupName = Placement, Name = "範囲X", Description = "アイテムを配置する範囲の幅")]
        [AnimationSlider("F0", "px", 0, 2000)]
        public Animation RangeX { get; } = new(1200, 0, 100000);

        [Display(GroupName = Placement, Name = "範囲Y", Description = "アイテムを配置する範囲の高さ")]
        [AnimationSlider("F0", "px", 0, 2000)]
        public Animation RangeY { get; } = new(700, 0, 100000);

        [Display(GroupName = Placement, Name = "範囲Z", Description = "アイテムを配置する範囲の奥行き")]
        [AnimationSlider("F0", "px", 0, 2000)]
        public Animation RangeZ { get; } = new(800, 0, 100000);

        [Display(GroupName = Placement, Name = "最大回転角", Description = "アイテムの最大回転角度")]
        [AnimationSlider("F0", "°", 0, 180)]
        public Animation MaxAngle { get; } = new(180, 0, 180);

        [Display(GroupName = Placement, Name = "大きさのばらつき", Description = "大きさをばらつかせる割合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation SizeVariation { get; } = new(0, 0, 100);

        [Display(GroupName = Placement, Name = "カメラに向ける", Description = "どれも常にカメラの方を向くようにします")]
        [ToggleSlider]
        public bool FacesCamera { get; set => Set(ref field, value); }

        [Display(GroupName = Placement, Name = "シード値", Description = "配置を決める乱数の種")]
        [TextBoxSlider("F0", "", 0, 1000)]
        [Range(0, int.MaxValue)]
        public int Seed { get; set => Set(ref field, Math.Max(0, value)); }

        [Display(GroupName = Movement, Name = "最大移動速度", Description = "ランダムに移動させる速さの上限で、範囲の端から出ると反対側から戻ります")]
        [AnimationSlider("F1", "px/f", 0, 20)]
        public Animation MaxSpeed { get; } = new(0, 0, 10000);

        [Display(GroupName = Movement, Name = "最大回転速度", Description = "ランダムに回転させる速さの上限")]
        [AnimationSlider("F1", "°/f", 0, 20)]
        public Animation MaxSpin { get; } = new(0, 0, 10000);

        [Display(GroupName = Movement, Name = "端でうすくする", Description = "範囲の端に近づいたものを薄くして、急に現れないようにします")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation EdgeFade { get; } = new(20, 0, 100);

        [Display(GroupName = Look, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit)); }

        [Display(GroupName = Look, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Look, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        internal SurfaceGloss GetGloss(in FrameContext time) => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        internal Vector3 GetRange(in FrameContext time) => Vector3.Max(new Vector3(RangeX.GetFloat(time), RangeY.GetFloat(time), RangeZ.GetFloat(time)), Vector3.Zero);

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new RandomScatter3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [RangeX, RangeY, RangeZ, MaxAngle, SizeVariation, MaxSpeed, MaxSpin, EdgeFade, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
