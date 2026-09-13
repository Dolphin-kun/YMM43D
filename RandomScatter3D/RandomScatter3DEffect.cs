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
    [VideoEffect("ランダム配置3D", ["3D"], ["ランダム配置", "複製", "ばらまく", "scatter", "雪", "紙吹雪"])]
    public sealed class RandomScatter3DEffect : VideoEffect3DBase
    {
        public const int MaxCount = 2000;

        private const string Placement = "ランダム配置3D";
        private const string Movement = "ランダム移動";
        private const string Look = "見た目";

        public override string Label => "ランダム配置3D";

        [Display(GroupName = Placement, Name = "配置数", Description = "配置する数", Order = 100)]
        [TextBoxSlider("F0", "個", 1, 200)]
        [Range(1, MaxCount)]
        public int Count { get => count; set => Set(ref count, Math.Clamp(value, 1, MaxCount)); }
        private int count = 30;

        [Display(GroupName = Placement, Name = "範囲X", Description = "アイテムを配置する範囲の幅", Order = 200)]
        [AnimationSlider("F0", "px", 0, 2000)]
        public Animation RangeX { get; } = new(1200, 0, 100000);

        [Display(GroupName = Placement, Name = "範囲Y", Description = "アイテムを配置する範囲の高さ", Order = 300)]
        [AnimationSlider("F0", "px", 0, 2000)]
        public Animation RangeY { get; } = new(700, 0, 100000);

        [Display(GroupName = Placement, Name = "範囲Z", Description = "アイテムを配置する範囲の奥行き", Order = 400)]
        [AnimationSlider("F0", "px", 0, 2000)]
        public Animation RangeZ { get; } = new(800, 0, 100000);

        [Display(GroupName = Placement, Name = "最大回転角", Description = "アイテムの最大回転角度", Order = 500)]
        [AnimationSlider("F0", "°", 0, 180)]
        public Animation MaxAngle { get; } = new(180, 0, 180);

        [Display(GroupName = Placement, Name = "大きさのばらつき",
            Description = "0 ですべて同じ大きさ、100 で 0 倍から等倍までばらつきます", Order = 600)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation SizeVariation { get; } = new(0, 0, 100);

        [Display(GroupName = Placement, Name = "カメラに向ける",
            Description = "入れると、どれも常にカメラの方を向きます。最大回転角は画面の中の回転にだけ効きます", Order = 700)]
        [ToggleSlider]
        public bool FacesCamera { get => facesCamera; set => Set(ref facesCamera, value); }
        private bool facesCamera;

        [Display(GroupName = Placement, Name = "シード値", Description = "変えると、同じ設定のまま配置だけが変わります", Order = 800)]
        [TextBoxSlider("F0", "", 0, 1000)]
        [Range(0, int.MaxValue)]
        public int Seed { get => seed; set => Set(ref seed, Math.Max(0, value)); }
        private int seed;

        [Display(GroupName = Movement, Name = "最大移動速度",
            Description = "ランダムに移動させます。範囲の端から出ると反対側から戻ります", Order = 100)]
        [AnimationSlider("F1", "px/f", 0, 20)]
        public Animation MaxSpeed { get; } = new(0, 0, 10000);

        [Display(GroupName = Movement, Name = "最大回転速度", Description = "ランダムに回転させます", Order = 200)]
        [AnimationSlider("F1", "°/f", 0, 20)]
        public Animation MaxSpin { get; } = new(0, 0, 10000);

        [Display(GroupName = Look, Name = "陰影をつけない", Description = "光源を無視して、元の絵のまま置きます", Order = 100)]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        [Display(GroupName = Look, Name = "つや", Description = "光が映り込んだ明るい点の強さ。0 でつやなし", Order = 200)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Look, Name = "つやの鋭さ", Description = "大きいほど映り込みが小さく締まり、磨いたように見えます", Order = 300)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        internal SurfaceGloss GetGloss(in FrameContext time)
            => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        internal Vector3 GetRange(in FrameContext time)
            => new(MathF.Max(RangeX.GetFloat(time), 0f), MathF.Max(RangeY.GetFloat(time), 0f), MathF.Max(RangeZ.GetFloat(time), 0f));

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new RandomScatter3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [RangeX, RangeY, RangeZ, MaxAngle, SizeVariation, MaxSpeed, MaxSpin, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
