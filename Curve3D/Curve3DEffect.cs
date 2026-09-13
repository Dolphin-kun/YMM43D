using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
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

        [Display(GroupName = Group, Name = "曲げ角度",
            Description = "端から端までで、これだけ回り込みます", Order = 100)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation BendAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "ねじり角度",
            Description = "反対の端までで、これだけひねります", Order = 200)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation TwistAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "軸の角度",
            Description = "どの向きに沿って曲げるか。0 で横、90 で縦", Order = 300)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation AxisAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "基準位置",
            Description = "曲げても動かない場所。50 で真ん中", Order = 400)]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Anchor { get; } = new(50, -1000, 1000);

        [Display(GroupName = Group, Name = "分割の細かさ",
            Description = "板を何枚の面で作るか。大きいほど滑らかで、そのぶん重くなります", Order = 500)]
        [TextBoxSlider("F0", "", 8, 128)]
        [Range(DeformGrid.MinSegments, DeformGrid.MaxSegments)]
        public int Segments
        {
            get => segments;
            set => Set(ref segments, Math.Clamp(value, DeformGrid.MinSegments, DeformGrid.MaxSegments));
        }
        private int segments = 64;

        [Display(GroupName = Group, Name = "陰影をつけない",
            Description = "光源を無視して、元の色のまま塗ります", Order = 600)]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        [Display(GroupName = Group, Name = "つや",
            Description = "光が映り込んだ明るい点の強さ。0 でつやなし", Order = 700)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Group, Name = "つやの鋭さ",
            Description = "大きいほど映り込みが小さく締まり、磨いたように見えます", Order = 800)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        internal SurfaceGloss GetGloss(in FrameContext time)
            => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Curve3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [BendAngle, TwistAngle, AxisAngle, Anchor, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CurveConstants
    {
        public float BendRadians;
        public float TwistRadians;
        public float AxisRadians;
        public float Anchor;
    }

    internal sealed class Curve3DProcessor(Curve3DEffect effect, IGraphicsDevicesAndContext devices)
        : Deform3DProcessorBase<CurveConstants>(effect, devices)
    {
        private readonly Curve3DEffect effect = effect;

        protected override string ShaderName => "Curve.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override SurfaceGloss GetGloss(in FrameContext time) => effect.GetGloss(time);

        protected override DeformGrid GetGrid(in FrameContext time)
            => DeformGrid.Create(effect.Segments, effect.Segments);

        protected override CurveConstants GetConstants(in FrameContext time) => new()
        {
            BendRadians = float.DegreesToRadians(effect.BendAngle.GetFloat(time)),
            TwistRadians = float.DegreesToRadians(effect.TwistAngle.GetFloat(time)),
            AxisRadians = float.DegreesToRadians(effect.AxisAngle.GetFloat(time)),
            Anchor = effect.Anchor.GetFloat(time) / 100f - 0.5f,
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            var bend = MathF.Abs(float.DegreesToRadians(effect.BendAngle.GetFloat(time)));

            var reach = bend > 1e-4f ? MathF.Min(2f / bend, 1.5f) : 0.75f;

            return new DeformExtent(reach, reach);
        }
    }
}
