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
    public enum WaveShape
    {
        [Display(Name = "横に流れる", Description = "左から右へ波が進みます")]
        Across,

        [Display(Name = "縦に流れる", Description = "上から下へ波が進みます")]
        Down,

        [Display(Name = "波紋", Description = "中心から外へ広がります")]
        Ripple,
    }

    [VideoEffect("波打ち3D", ["3D"], [])]
    public class Wave3DEffect : VideoEffect3DBase
    {
        private const string Group = "波打ち3D";

        public override string Label => "波打ち3D";

        [Display(GroupName = Group, Name = "高さ", Description = "波の山と谷の、手前と奥への振れ幅")]
        [AnimationSlider("F1", "px", 0, 200)]
        public Animation Height { get; } = new(30, 0, 100000);

        [Display(GroupName = Group, Name = "波の間隔", Description = "山から次の山までの長さ")]
        [AnimationSlider("F1", "px", 10, 2000)]
        public Animation Wavelength { get; } = new(300, 1, 100000);

        [Display(GroupName = Group, Name = "位相", Description = "波の位置。時間で動かすと流れます")]
        [AnimationSlider("F1", "°", 0, 720)]
        public Animation Phase { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Group, Name = "形", Description = "波の進む向き")]
        [EnumComboBox]
        public WaveShape Shape { get => shape; set => Set(ref shape, value); }
        private WaveShape shape = WaveShape.Across;

        [Display(GroupName = Group, Name = "分割の細かさ",
            Description = "板を何枚の面で作るか。波の間隔が狭いときは大きくしてください")]
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
            => AttachProcessor(new Wave3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Height, Wavelength, Phase, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveConstants
    {
        public float Amplitude;
        public float Wavelength;
        public float PhaseRadians;
        public int Mode;
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
            PhaseRadians = Rotation3D.ToRadians(effect.Phase.GetFloat(time)),
            Mode = (int)effect.Shape,
        };

        protected override DeformExtent GetExtent(in FrameContext time)
            => new(0f, MathF.Min(ToPlaneUnits(effect.Height.GetFloat(time)), 4f));

        // シェーダーは板の幅を 1 とした物差しで動くので、px をそこへ直す。
        private float ToPlaneUnits(float pixels)
            => TryGetSize(out var size, out _) && size.X > 0f ? pixels / size.X : 0f;
    }
}
