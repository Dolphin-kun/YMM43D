using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Fold3D
{
    [VideoEffect("折る3D", ["3D"], ["折る", "蛇腹", "fold", "紙"])]
    public class Fold3DEffect : VideoEffect3DBase
    {
        private const string Group = "折る3D";

        public override string Label => "折る3D";

        [Display(GroupName = Group, Name = "折り込み",
            Description = "0 で平ら、大きくするほど蛇腹に畳まれます", Order = 100)]
        [AnimationSlider("F1", "°", 0, 170)]
        public Animation Angle { get; } = new(0, 0, 179);

        [Display(GroupName = Group, Name = "折り目の数",
            Description = "何段に折るか", Order = 200)]
        [TextBoxSlider("F0", "", 2, 32)]
        [Range(1, 64)]
        public int Count
        {
            get => count;
            set => Set(ref count, Math.Clamp(value, 1, 64));
        }
        private int count = 6;

        [Display(GroupName = Group, Name = "軸の角度",
            Description = "どの向きに折るか。0 で横、90 で縦", Order = 300)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation AxisAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "陰影をつけない",
            Description = "光源を無視して、元の色のまま塗ります", Order = 400)]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Fold3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Angle, AxisAngle, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FoldConstants
    {
        public float HalfAngle;
        public float Count;
        public float AxisRadians;
        public float Padding;
    }

    internal sealed class Fold3DProcessor(Fold3DEffect effect, IGraphicsDevicesAndContext devices)
        : Deform3DProcessorBase<FoldConstants>(effect, devices)
    {
        private const int SegmentsPerFold = 4;

        private readonly Fold3DEffect effect = effect;

        protected override string ShaderName => "Fold.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        // 折り目の位置に頂点が来ないと角が丸まる。斜めにも折れるので、
        // 縦横どちらにも段の数ぶんの細かさを持たせる。
        protected override DeformGrid GetGrid(in FrameContext time)
        {
            var along = effect.Count * SegmentsPerFold;

            return DeformGrid.Create(along, along);
        }

        protected override FoldConstants GetConstants(in FrameContext time) => new()
        {
            HalfAngle = Rotation3D.ToRadians(effect.Angle.GetFloat(time)) / 2f,
            Count = effect.Count,
            AxisRadians = Rotation3D.ToRadians(effect.AxisAngle.GetFloat(time)),
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            // 畳むほど横は縮み、奥行きは 1段ぶんまでしか出ない。
            var rise = MathF.Sin(Rotation3D.ToRadians(effect.Angle.GetFloat(time)) / 2f)
                     / MathF.Max(effect.Count, 1);

            return new DeformExtent(0f, rise);
        }
    }
}
