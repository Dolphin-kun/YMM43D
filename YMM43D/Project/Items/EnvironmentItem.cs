using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Project.Items
{
    public sealed class EnvironmentItem : BaseItem, ISceneEnvironment
    {
        private const string Ambient = "環境光";

        private const string Fog = "霧";

        private const int FirstOrder = 100;

        [Display(GroupName = Ambient, Name = "色",
            Description = "光の当たらない面にも回り込む光の色", Order = FirstOrder)]
        [ColorPicker]
        public Color AmbientColor { get => ambientColor; set => Set(ref ambientColor, value); }
        private Color ambientColor = Colors.White;

        [Display(GroupName = Ambient, Name = "明るさ",
            Description = "0 にすると光の当たらない面は真っ黒になります", Order = FirstOrder + 1)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation AmbientBrightness { get; } = new(SceneLighting.DefaultAmbient * 100, 0, 10000);

        [Display(GroupName = Fog, Name = "霧をかける",
            Description = "遠くのものほど霧の色に溶かします", Order = FirstOrder + 2)]
        [ToggleSlider]
        public bool IsFogEnabled { get => isFogEnabled; set => Set(ref isFogEnabled, value); }
        private bool isFogEnabled;

        [Display(GroupName = Fog, Name = "色", Order = FirstOrder + 3)]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Color FogColor { get => fogColor; set => Set(ref fogColor, value); }
        private Color fogColor = Color.FromRgb(0x20, 0x24, 0x2C);

        [Display(GroupName = Fog, Name = "濃さ",
            Description = "いちばん奥でどれだけ霧の色になるか", Order = FirstOrder + 4)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Animation FogDensity { get; } = new(100, 0, 100);

        [Display(GroupName = Fog, Name = "始まる距離",
            Description = "カメラからこの距離までは霧がかかりません", Order = FirstOrder + 5)]
        [AnimationSlider("F1", "px", 0, 5000)]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Animation FogStart { get; } = new(1000, 0, 1000000);

        [Display(GroupName = Fog, Name = "届く距離",
            Description = "この距離で霧が濃さいっぱいになります", Order = FirstOrder + 6)]
        [AnimationSlider("F1", "px", 100, 10000)]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Animation FogEnd { get; } = new(4000, 1, 1000000);

        public override string Label => "3D環境";

        public override Color ItemColor
        {
            get => itemColor;
            set => Set(ref itemColor, value);
        }
        private Color itemColor = Color.FromRgb(0x6C, 0xA8, 0xD8);

        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        public Vector3 GetAmbient(in FrameContext itemTime)
            => ToLinear(AmbientColor) * (AmbientBrightness.GetFloat(itemTime) / 100f);

        public SceneFog GetFog(in FrameContext itemTime)
        {
            if (!IsFogEnabled)
                return SceneFog.None;

            var start = WorldScale.ToWorld(FogStart.GetFloat(itemTime));
            var end = WorldScale.ToWorld(FogEnd.GetFloat(itemTime));

            return new SceneFog(
                ToLinear(FogColor),
                FogDensity.GetFloat(itemTime) / 100f,
                start,
                MathF.Max(end, start + 1e-3f));
        }

        private static Vector3 ToLinear(Color color)
            => new(color.R / 255f, color.G / 255f, color.B / 255f);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [AmbientBrightness, FogDensity, FogStart, FogEnd];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription)
            => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }
}
