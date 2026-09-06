using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Deform3D
{
    public enum DeformAxis
    {
        [Display(Name = "横", Description = "左右の向きに沿って変形します")]
        Across,

        [Display(Name = "縦", Description = "上下の向きに沿って変形します")]
        Down,
    }

    [VideoEffect("湾曲3D", ["3D"], [])]
    public class Curve3DEffect : VideoEffect3DBase
    {
        private const string Group = "湾曲3D";

        public override string Label => "湾曲3D";

        [Display(GroupName = Group, Name = "曲げ", Description = "端から端までで、これだけ回り込みます")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation Bend { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "ねじり", Description = "反対側の端までで、これだけひねります")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation Twist { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "向き", Description = "どちらの向きに沿って曲げるか")]
        [EnumComboBox]
        public DeformAxis Axis { get => axis; set => Set(ref axis, value); }
        private DeformAxis axis = DeformAxis.Across;

        [Display(GroupName = Group, Name = "分割の細かさ",
            Description = "板を何枚の面で作るか。大きいほど滑らかで、そのぶん重くなります")]
        [TextBoxSlider("F0", "", 8, 128)]
        public int Segments
        {
            get => segments;
            set => Set(ref segments, Math.Clamp(value, DeformGrid.MinSegments, DeformGrid.MaxSegments));
        }
        private int segments = 64;

        [Display(GroupName = Group, Name = "陰影をつけない",
            Description = "光源を無視して、元の色のまま塗ります")]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Curve3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Bend, Twist, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CurveConstants
    {
        public float BendRadians;
        public float TwistRadians;
        public int AlongY;
        public int Padding;
    }

    internal sealed class Curve3DProcessor(Curve3DEffect effect, IGraphicsDevicesAndContext devices)
        : Deform3DProcessorBase<CurveConstants>(effect, devices)
    {
        private readonly Curve3DEffect effect = effect;

        protected override string ShaderName => "Curve.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override DeformGrid GetGrid(in FrameContext time)
            => DeformGrid.Create(effect.Segments, effect.Segments);

        protected override CurveConstants GetConstants(in FrameContext time) => new()
        {
            BendRadians = Rotation3D.ToRadians(effect.Bend.GetFloat(time)),
            TwistRadians = Rotation3D.ToRadians(effect.Twist.GetFloat(time)),
            AlongY = effect.Axis == DeformAxis.Down ? 1 : 0,
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            var bend = MathF.Abs(Rotation3D.ToRadians(effect.Bend.GetFloat(time)));

            // 円弧に置き換えたときの、いちばん遠いところ。
            // 曲げが小さいと半径が跳ね上がるので、平らなときの大きさで頭を打たせる。
            var reach = bend > 1e-4f ? 2f / bend : 0.5f;

            return new DeformExtent(MathF.Min(reach, 1.5f), MathF.Min(reach, 1.5f));
        }
    }
}
