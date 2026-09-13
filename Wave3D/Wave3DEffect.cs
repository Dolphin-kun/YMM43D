using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Wave3D
{
    [VideoEffect("波打ち3D", ["3D"], ["波", "うねり", "wave", "旗", "水面"])]
    public class Wave3DEffect : VideoEffect3DBase
    {
        private const string Group = "波打ち3D";

        public override string Label => "波打ち3D";

        [Display(GroupName = Group, Name = "高さ",
            Description = "波の山と谷の、手前と奥への振れ幅", Order = 100)]
        [AnimationSlider("F1", "px", 0, 200)]
        public Animation Height { get; } = new(30, 0, 100000);

        [Display(GroupName = Group, Name = "波の間隔",
            Description = "山から次の山までの長さ", Order = 200)]
        [AnimationSlider("F1", "px", 10, 2000)]
        public Animation Wavelength { get; } = new(300, 1, 100000);

        [Display(GroupName = Group, Name = "位相",
            Description = "波の位置。時間で動かすと流れます", Order = 300)]
        [AnimationSlider("F1", "°", 0, 720)]
        public Animation Phase { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Group, Name = "軸の角度",
            Description = "波の進む向き。0 で横、90 で縦", Order = 400)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation AxisAngle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "波紋",
            Description = "向きに沿わせず、中心から外へ広げます", Order = 500)]
        [ToggleSlider]
        public bool IsRipple { get => isRipple; set => Set(ref isRipple, value); }
        private bool isRipple;

        [Display(GroupName = Group, Name = "分割の細かさ",
            Description = "板を何枚の面で作るか。波の間隔が狭いときは大きくしてください", Order = 600)]
        [TextBoxSlider("F0", "", 8, 128)]
        [Range(DeformGrid.MinSegments, DeformGrid.MaxSegments)]
        public int Segments
        {
            get => segments;
            set => Set(ref segments, Math.Clamp(value, DeformGrid.MinSegments, DeformGrid.MaxSegments));
        }
        private int segments = 64;

        [Display(GroupName = Group, Name = "陰影をつけない",
            Description = "光源を無視して、元の色のまま塗ります", Order = 700)]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Wave3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Height, Wavelength, Phase, AxisAngle, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveConstants
    {
        public float Amplitude;
        public float Wavelength;
        public float PhaseRadians;
        public float AxisRadians;

        public int Ripple;
        public int Padding0;
        public int Padding1;
        public int Padding2;
    }

    internal sealed class Wave3DProcessor(Wave3DEffect effect, IGraphicsDevicesAndContext devices)
        : Deform3DProcessorBase<WaveConstants>(effect, devices)
    {
        private readonly Wave3DEffect effect = effect;

        protected override string ShaderName => "Wave.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        protected override DeformGrid GetGrid(in FrameContext time)
            => DeformGrid.Create(effect.Segments, effect.Segments);

        protected override WaveConstants GetConstants(in FrameContext time) => new()
        {
            Amplitude = ToPlaneUnits(effect.Height.GetFloat(time)),
            Wavelength = ToPlaneUnits(effect.Wavelength.GetFloat(time)),
            PhaseRadians = float.DegreesToRadians(effect.Phase.GetFloat(time)),
            AxisRadians = float.DegreesToRadians(effect.AxisAngle.GetFloat(time)),
            Ripple = effect.IsRipple ? 1 : 0,
        };

        protected override DeformExtent GetExtent(in FrameContext time)
            => new(0f, MathF.Min(ToPlaneUnits(effect.Height.GetFloat(time)), 4f));

        private float ToPlaneUnits(float pixels)
            => TryGetSize(out var size, out _) && size.X > 0f ? pixels / size.X : 0f;
    }
}
