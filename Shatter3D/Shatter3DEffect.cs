using System.ComponentModel;
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

namespace Shatter3D
{
    [VideoEffect("砕け散る3D", ["3D"], ["砕け散る", "クラッシュ", "crash", "割れ", "ガラス"])]
    public class Shatter3DEffect : VideoEffect3DBase
    {
        private const string Group = "砕け散る3D";
        private const string FromGroup = "どこから";
        private const string DetailGroup = "動きの詳細";

        public override string Label => "砕け散る3D";

        [Display(GroupName = Group, Name = "開始時間",
            Description = "アイテムの頭から何秒後に砕けるか", Order = 100)]
        [TextBoxSlider("F2", "秒", -10, 10)]
        [Range(-100000, 100000)]
        [DefaultValue(0d)]
        public double StartTime { get => startTime; set => Set(ref startTime, value); }
        private double startTime;

        [Display(GroupName = Group, Name = "再生速度",
            Description = "砕けが進む速さ", Order = 200)]
        [TextBoxSlider("F1", "%", -100, 100)]
        [Range(-100000, 100000)]
        [DefaultValue(100d)]
        public double PlaybackRate { get => playbackRate; set => Set(ref playbackRate, value); }
        private double playbackRate = 100;

        [Display(GroupName = Group, Name = "破片の大きさ",
            Description = "1枚あたりの差し渡し。小さいほど細かく割れます", Order = 300)]
        [TextBoxSlider("F1", "px", 4, 200)]
        [Range(1, 100000)]
        [DefaultValue(50d)]
        public double Size { get => size; set => Set(ref size, Math.Max(1, value)); }
        private double size = 50;

        [Display(GroupName = FromGroup, Name = "X", Description = "衝撃が来る位置", Order = 100)]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation X { get; } = new(0, -100000, 100000);

        [Display(GroupName = FromGroup, Name = "Y", Description = "衝撃が来る位置", Order = 200)]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Y { get; } = new(0, -100000, 100000);

        [Display(GroupName = FromGroup, Name = "Z", Description = "衝撃が来る位置。手前が正", Order = 300)]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Z { get; } = new(0, -100000, 100000);

        [Display(GroupName = DetailGroup, Name = "飛ぶ速さ", Order = 100)]
        [TextBoxSlider("F1", "%", 0, 200)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double FlySpeed { get => flySpeed; set => Set(ref flySpeed, value); }
        private double flySpeed = 100;

        [Display(GroupName = DetailGroup, Name = "落ちる速さ", Order = 200)]
        [TextBoxSlider("F1", "%", 0, 200)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double FallSpeed { get => fallSpeed; set => Set(ref fallSpeed, value); }
        private double fallSpeed = 100;

        [Display(GroupName = DetailGroup, Name = "遅れ",
            Description = "破片ごとに飛び始めをずらす量", Order = 300)]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double Delay { get => delay; set => Set(ref delay, value); }
        private double delay = 100;

        [Display(GroupName = DetailGroup, Name = "衝撃",
            Description = "当たったところに近い破片ほど強く弾く量", Order = 400)]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double Impact { get => impact; set => Set(ref impact, value); }
        private double impact = 100;

        [Display(GroupName = DetailGroup, Name = "回転のばらつき", Order = 500)]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double RandomRotate { get => randomRotate; set => Set(ref randomRotate, value); }
        private double randomRotate = 100;

        [Display(GroupName = DetailGroup, Name = "方向のばらつき", Order = 600)]
        [TextBoxSlider("F1", "%", 0, 100)]
        [Range(0, 100000)]
        [DefaultValue(100d)]
        public double RandomVector { get => randomVector; set => Set(ref randomVector, value); }
        private double randomVector = 100;

        [Display(GroupName = DetailGroup, Name = "陰影をつけない",
            Description = "光源を無視して、元の色のまま塗ります", Order = 700)]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Shatter3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [X, Y, Z, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShatterConstants
    {
        public float Seconds;
        public float FlySpeed;
        public float FallSpeed;
        public float Delay;

        public Vector3 ImpactPoint;
        public float Impact;

        public float RandomRotate;
        public float RandomVector;
        public float SpinRate;
        public float Padding;
    }

    internal sealed class Shatter3DProcessor(Shatter3DEffect effect, IGraphicsDevicesAndContext devices)
        : Deform3DProcessorBase<ShatterConstants>(effect, devices)
    {
        // 100% のときの目安。板の幅を 1 として、1秒でどれだけ動くか。
        private const float FlyPerSecond = 1.2f;
        private const float FallPerSecond = 1.6f;
        private const float SpinPerSecond = 6f;
        private const float DelaySeconds = 0.6f;

        private readonly Shatter3DEffect effect = effect;

        protected override string ShaderName => "Shatter.hlsl";

        protected override bool IsUnlit => effect.IsUnlit;

        // 破片の大きさ(px)から、板を何マスに割るかを決める。
        protected override DeformGrid GetGrid(in FrameContext time)
        {
            if (!TryGetSize(out var size, out _) || size.X <= 0f || size.Y <= 0f)
                return DeformGrid.Create(16, 16, separated: true);

            var piece = (float)Math.Max(effect.Size, 1);

            return DeformGrid.Create(
                (int)MathF.Ceiling(size.X / piece),
                (int)MathF.Ceiling(size.Y / piece),
                separated: true);
        }

        protected override ShatterConstants GetConstants(in FrameContext time) => new()
        {
            Seconds = ElapsedSeconds(time),
            FlySpeed = FlyPerSecond * Percent(effect.FlySpeed),
            FallSpeed = FallPerSecond * Percent(effect.FallSpeed),
            Delay = DelaySeconds * Percent(effect.Delay),
            ImpactPoint = ImpactPoint(time),
            Impact = Percent(effect.Impact),
            RandomRotate = Percent(effect.RandomRotate),
            RandomVector = Percent(effect.RandomVector),
            SpinRate = SpinPerSecond,
        };

        protected override DeformExtent GetExtent(in FrameContext time)
        {
            var seconds = ElapsedSeconds(time);
            if (seconds <= 0f)
                return new DeformExtent(0f, 0f);

            var flown = FlyPerSecond * Percent(effect.FlySpeed)
                      * (1f + Percent(effect.Impact)) * seconds;

            var fallen = FallPerSecond * Percent(effect.FallSpeed) * seconds * seconds;

            var reach = MathF.Min(flown + fallen, 16f);

            return new DeformExtent(reach, MathF.Min(flown, 16f));
        }

        // アイテムの頭から数えた秒数を、開始時間と再生速度で読み替える。
        private float ElapsedSeconds(in FrameContext time)
        {
            var seconds = (float)time.Frame / Math.Max(time.Fps, 1);

            return (seconds - (float)effect.StartTime) * Percent(effect.PlaybackRate);
        }

        private Vector3 ImpactPoint(in FrameContext time)
        {
            if (!TryGetSize(out var size, out _) || size.X <= 0f || size.Y <= 0f)
                return Vector3.Zero;

            // 板の横は幅で、縦は高さで -0.5〜0.5 に収まっている。奥行きは横に合わせる。
            return new Vector3(
                effect.X.GetFloat(time) / size.X,
                -effect.Y.GetFloat(time) / size.Y,
                effect.Z.GetFloat(time) / size.X);
        }

        private static float Percent(double value) => (float)value / 100f;
    }
}
