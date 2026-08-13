using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Lighting;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Items
{
    public sealed class LightItem : BaseItem, ISceneLightSource
    {
        private const string Group = "3D光源";

        private const int FirstOrder = 100;

        [Display(GroupName = Group, Name = "種類",
            Description = "平行光は向きだけ、点光源は置いた場所から周りを照らします", Order = FirstOrder)]
        [EnumComboBox]
        public LightKind Kind { get => kind; set => Set(ref kind, value); }
        private LightKind kind = LightKind.Directional;

        [Display(GroupName = Group, Name = "水平角",
            Description = "光が来る向き。0 で正面から", Order = FirstOrder + 1)]
        [AnimationSlider("F1", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(Kind), LightKind.Directional)]
        public Animation Yaw { get; } = new(SceneLighting.DefaultYaw, -100000, 100000);

        [Display(GroupName = Group, Name = "垂直角",
            Description = "光が来る高さ。正で上から", Order = FirstOrder + 2)]
        [AnimationSlider("F1", "°", -90, 90)]
        [ShowPropertyEditorWhen(nameof(Kind), LightKind.Directional)]
        public Animation Pitch { get; } = new(SceneLighting.DefaultPitch, -90, 90);

        [Display(GroupName = Group, Name = "X", Description = "光を置く位置。右が正", Order = FirstOrder + 3)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(Kind), LightKind.Point)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Group, Name = "Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 4)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(Kind), LightKind.Point)]
        public Animation Y { get; } = new(-500, -1000000, 1000000);

        [Display(GroupName = Group, Name = "Z", Description = "手前が正", Order = FirstOrder + 5)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(Kind), LightKind.Point)]
        public Animation Z { get; } = new(500, -1000000, 1000000);

        [Display(GroupName = Group, Name = "届く距離",
            Description = "この距離まで届きます。遠いほど弱くなります", Order = FirstOrder + 6)]
        [AnimationSlider("F1", "px", 100, 5000)]
        [ShowPropertyEditorWhen(nameof(Kind), LightKind.Point)]
        public Animation Reach { get; } = new(2000, 1, 1000000);

        [Display(GroupName = Group, Name = "色", Order = FirstOrder + 7)]
        [ColorPicker]
        public Color LightColor { get => lightColor; set => Set(ref lightColor, value); }
        private Color lightColor = Colors.White;

        [Display(GroupName = Group, Name = "明るさ", Order = FirstOrder + 8)]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Brightness { get; } = new(SceneLighting.DefaultBrightness * 100, 0, 10000);

        public override string Label => "3D光源";

        public override Color ItemColor
        {
            get => itemColor;
            set => Set(ref itemColor, value);
        }
        private Color itemColor = Color.FromRgb(0xFF, 0xD5, 0x4F);

        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        public SceneLight GetLight(in FrameContext itemTime)
        {
            var color = ToLinear(LightColor) * (Brightness.GetFloat(itemTime) / 100f);

            if (Kind == LightKind.Point)
            {
                var position = new Vector3(
                    WorldScale.ToWorld(X.GetFloat(itemTime)),
                    -WorldScale.ToWorld(Y.GetFloat(itemTime)),
                    WorldScale.ToWorld(Z.GetFloat(itemTime)));

                return SceneLight.Point(position, color, WorldScale.ToWorld(Reach.GetFloat(itemTime)));
            }

            return SceneLight.FromAngles(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime), color);
        }

        private static Vector3 ToLinear(Color color)
            => new(color.R / 255f, color.G / 255f, color.B / 255f);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Yaw, Pitch, X, Y, Z, Reach, Brightness];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription)
            => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }
}
