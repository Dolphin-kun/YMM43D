using System.ComponentModel.DataAnnotations;
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
    [VideoEffect("砕け散る3D", ["3D"], [])]
    public class Shatter3DEffect : VideoEffect3DBase
    {
        private const string Group = "砕け散る3D";

        public override string Label => "砕け散る3D";

        [Display(GroupName = Group, Name = "進み具合",
            Description = "0 で元のまま、100 で飛び散り切ります")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Progress { get; } = new(0, 0, 100);

        [Display(GroupName = Group, Name = "飛ぶ距離", Description = "破片が中心から離れていく距離")]
        [AnimationSlider("F1", "px", 0, 2000)]
        public Animation Distance { get; } = new(400, 0, 100000);

        [Display(GroupName = Group, Name = "回転", Description = "破片が飛びながら回る量")]
        [AnimationSlider("F1", "°", 0, 1080)]
        public Animation Spin { get; } = new(180, 0, 100000);

        [Display(GroupName = Group, Name = "落下", Description = "飛びながら下へ引かれる量")]
        [AnimationSlider("F1", "px", 0, 2000)]
        public Animation Gravity { get; } = new(300, 0, 100000);

        [Display(GroupName = Group, Name = "ばらけ方",
            Description = "0 で全部いっせいに、大きくするほど飛び始める時刻がずれます")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Stagger { get; } = new(50, 0, 100);

        [Display(GroupName = Group, Name = "破片の数", Description = "横と縦に、それぞれ何枚に割るか")]
        [TextBoxSlider("F0", "", 4, 64)]
        public int Pieces
        {
            get => pieces;
            set => Set(ref pieces, Math.Clamp(value, 1, DeformGrid.MaxSegments));
        }
        private int pieces = 16;

        [Display(GroupName = Group, Name = "散らし方",
            Description = "同じ数字なら毎回同じ散り方になります")]
        [TextBoxSlider("F0", "", 0, 100)]
        public int Seed { get => seed; set => Set(ref seed, Math.Clamp(value, 0, 10000)); }
        private int seed;

        [Display(GroupName = Group, Name = "陰影をつけない",
            Description = "光源を無視して、元の色のまま塗ります")]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Shatter3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Progress, Distance, Spin, Gravity, Stagger, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShatterConstants
    {
        public float Progress;
        public float FlightDistance;
        public float Spin;
        public float Gravity;

        public float Seed;
        public float Stagger;
        public int Padding0;
        public int Padding1;
    }

    internal sealed class Shatter3DProcessor(Shatter3DEffect effect, IGraphicsDevicesAndContext devices)
        : Deform3DProcessorBase<ShatterConstants>(effect, devices)
    {
        private readonly Shatter3DEffect effect = effect;

        protected override string ShaderName => "Shatter.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override DeformGrid GetGrid(in FrameContext time)
            => DeformGrid.Create(effect.Pieces, effect.Pieces, separated: true);

        protected override ShatterConstants GetConstants(in FrameContext time) => new()
        {
            Progress = effect.Progress.GetFloat(time) / 100f,
            FlightDistance = ToPlaneUnits(effect.Distance.GetFloat(time)),
            Spin = Rotation3D.ToRadians(effect.Spin.GetFloat(time)),
            Gravity = ToPlaneUnits(effect.Gravity.GetFloat(time)),
            Seed = effect.Seed,
            Stagger = Math.Clamp(effect.Stagger.GetFloat(time) / 100f, 0f, 0.95f),
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            if (effect.Progress.GetFloat(time) <= 0f)
                return new DeformExtent(0f, 0f);

            var distance = ToPlaneUnits(effect.Distance.GetFloat(time));
            var gravity = ToPlaneUnits(effect.Gravity.GetFloat(time));

            var reach = MathF.Min(distance * 1.5f + gravity, 8f);

            return new DeformExtent(reach, MathF.Min(distance, 8f));
        }

        private float ToPlaneUnits(float pixels)
            => TryGetSize(out var size, out _) && size.X > 0f ? pixels / size.X : 0f;
    }
}
