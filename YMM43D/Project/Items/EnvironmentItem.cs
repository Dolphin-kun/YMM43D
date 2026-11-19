using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Items
{
    public sealed class EnvironmentItem : BaseItem, ISceneEnvironment
    {
        private const string Ambient = "環境光";
        private const string Fog = "霧";
        private const string Mirror = "映り込み";
        private const string Shade = "影";

        private const int AfterGeneral = 100;

        [Display(GroupName = Ambient, Name = "色", Description = "光の当たらない面にも回り込む光の色", Order = AfterGeneral)]
        [ColorPicker]
        public Color AmbientColor { get => ambientColor; set => Set(ref ambientColor, value); }
        private Color ambientColor = Colors.White;

        [Display(GroupName = Ambient, Name = "明るさ", Description = "光の当たらない面にも回り込む光の明るさ", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation AmbientBrightness { get; } = new(SceneLighting.DefaultAmbient * 100, 0, 10000);

        [Display(GroupName = Ambient, Name = "下からも当てる", Description = "上からと下からで、回り込む光の色を分けます", Order = AfterGeneral)]
        [ToggleSlider]
        public bool HasGround { get; set => Set(ref field, value); }

        [Display(GroupName = Ambient, Name = "下からの色", Description = "下向きの面に当たる光の色", Order = AfterGeneral)]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(HasGround), true)]
        public Color GroundColor { get => groundColor; set => Set(ref groundColor, value); }
        private Color groundColor = Color.FromRgb(0x40, 0x3A, 0x34);

        [Display(GroupName = Ambient, Name = "下からの明るさ", Description = "下向きの面に当たる光の明るさ", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(HasGround), true)]
        public Animation GroundBrightness { get; } = new(20, 0, 10000);

        [Display(GroupName = Fog, Name = "霧をかける", Description = "遠くのものほど霧の色に溶かします", Order = AfterGeneral)]
        [ToggleSlider]
        public bool IsFogEnabled { get; set => Set(ref field, value); }

        [Display(GroupName = Fog, Name = "色", Description = "霧の色", Order = AfterGeneral)]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Color FogColor { get => fogColor; set => Set(ref fogColor, value); }
        private Color fogColor = Color.FromRgb(0x20, 0x24, 0x2C);

        [Display(GroupName = Fog, Name = "濃さ", Description = "いちばん奥でどれだけ霧の色になるか", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Animation FogDensity { get; } = new(100, 0, 100);

        [Display(GroupName = Fog, Name = "始まる距離", Description = "カメラからこの距離までは霧がかかりません", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", 0, 5000)]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Animation FogStart { get; } = new(1000, 0, 1000000);

        [Display(GroupName = Fog, Name = "届く距離", Description = "この距離で霧が濃さいっぱいになります", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", 100, 10000)]
        [ShowPropertyEditorWhen(nameof(IsFogEnabled), true)]
        public Animation FogEnd { get; } = new(4000, 1, 1000000);

        [Display(GroupName = Mirror, Name = "風景の画像", Description = "まわりの景色を左右 360 度・上下 180 度に引き伸ばした横長の画像で、3Dモデルの「映り込み」に使います", Order = AfterGeneral)]
        [FileSelector(FileGroupType.ImageItem)]
        public string MirrorImage { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        [Display(GroupName = Mirror, Name = "回り込む光に混ぜる", Description = "風景の色を、光の当たらないところの明るさにも混ぜます", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation MirrorAmbient { get; } = new(0, 0, 100);

        [Display(GroupName = Shade, Name = "影の細かさ", Description = "影を描く板の解像度", Order = AfterGeneral)]
        [EnumComboBox]
        public ShadowQuality ShadowQuality { get; set => Set(ref field, value); } = ShadowQuality.Normal;

        int ISceneEnvironment.ShadowResolution => ShadowQuality.Resolution();

        public override string Label => "3D環境";

        public override Color ItemColor { get => itemColor; set => Set(ref itemColor, value); }
        private Color itemColor = Color.FromRgb(0x6C, 0xA8, 0xD8);

        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        public Vector3 GetAmbient(in FrameContext itemTime) => AmbientColor.ToVector3() * (AmbientBrightness.GetFloat(itemTime) / 100f);

        public Vector3? GetGround(in FrameContext itemTime) => HasGround ? GroundColor.ToVector3() * (GroundBrightness.GetFloat(itemTime) / 100f) : null;

        public SceneMirror GetMirror(in FrameContext itemTime) => MirrorImage.Length == 0 ? SceneMirror.None : new SceneMirror(MirrorImage, MirrorAmbient.GetFloat(itemTime) / 100f);

        public SceneFog GetFog(in FrameContext itemTime)
        {
            if (!IsFogEnabled)
                return SceneFog.None;

            var start = WorldScale.ToWorld(FogStart.GetFloat(itemTime));
            var end = WorldScale.ToWorld(FogEnd.GetFloat(itemTime));

            return new SceneFog(FogColor.ToVector3(), FogDensity.GetFloat(itemTime) / 100f, start, MathF.Max(end, start + 1e-3f));
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [AmbientBrightness, GroundBrightness, MirrorAmbient, FogDensity, FogStart, FogEnd];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription) => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => MirrorImage.Length > 0 ? [MirrorImage] : [];

        public override void ReplaceFile(string from, string to)
        {
            if (MirrorImage == from)
                MirrorImage = to;
        }
    }
}
