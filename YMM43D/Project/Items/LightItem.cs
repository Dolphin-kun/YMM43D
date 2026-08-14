using System.ComponentModel;
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
    public sealed class LightItem : BaseItem, ISceneLightSource, ISceneMarkerSource
    {
        private const string Lamp = "3D光源";

        private const int FirstOrder = 100;

        [Display(GroupName = Lamp, Name = "種類",
            Description = "平行光は向きだけ、点光源は置いた場所から周りを照らします", Order = FirstOrder)]
        [EnumComboBox]
        public LightKind Kind
        {
            get => kind;
            set
            {
                Set(ref kind, value);
                OnPropertyChanged(nameof(IsDirectional));
                OnPropertyChanged(nameof(IsPoint));
            }
        }
        private LightKind kind = LightKind.Directional;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsDirectional => Kind == LightKind.Directional;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsPoint => Kind == LightKind.Point;

        [Display(GroupName = Lamp, Name = "水平角",
            Description = "光が来る向き。0 で正面から", Order = FirstOrder + 1)]
        [AnimationSlider("F1", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(IsDirectional), true)]
        public Animation Yaw { get; } = new(SceneLighting.DefaultYaw, -100000, 100000);

        [Display(GroupName = Lamp, Name = "垂直角",
            Description = "光が来る高さ。正で上から", Order = FirstOrder + 2)]
        [AnimationSlider("F1", "°", -90, 90)]
        [ShowPropertyEditorWhen(nameof(IsDirectional), true)]
        public Animation Pitch { get; } = new(SceneLighting.DefaultPitch, -90, 90);

        [Display(GroupName = Lamp, Name = "X", Description = "光を置く位置。右が正", Order = FirstOrder + 3)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(IsPoint), true)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 4)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(IsPoint), true)]
        public Animation Y { get; } = new(-500, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "Z", Description = "手前が正", Order = FirstOrder + 5)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(IsPoint), true)]
        public Animation Z { get; } = new(500, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "届く距離",
            Description = "この距離まで届きます。遠いほど弱くなります", Order = FirstOrder + 6)]
        [AnimationSlider("F1", "px", 100, 5000)]
        [ShowPropertyEditorWhen(nameof(IsPoint), true)]
        public Animation Reach { get; } = new(2000, 1, 1000000);

        [Display(GroupName = Lamp, Name = "色", Order = FirstOrder + 7)]
        [ColorPicker]
        public Color LightColor { get => lightColor; set => Set(ref lightColor, value); }
        private Color lightColor = Colors.White;

        [Display(GroupName = Lamp, Name = "明るさ", Order = FirstOrder + 8)]
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
                return SceneLight.Point(
                    GetPosition(itemTime), color, WorldScale.ToWorld(Reach.GetFloat(itemTime)));
            }

            return SceneLight.FromAngles(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime), color);
        }

        public SceneMarker GetMarker(in FrameContext itemTime)
            => Kind == LightKind.Point
                ? SceneMarker.ForPointLight(GetPosition(itemTime), WorldScale.ToWorld(Reach.GetFloat(itemTime)))
                : SceneMarker.ForDirectionalLight(
                    SceneLight.ToDirection(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime)));

        public void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope)
        {
            if (Kind == LightKind.Point)
            {
                scope.Nudge(X, WorldScale.ToPixels(shift.X));
                scope.Nudge(Y, -WorldScale.ToPixels(shift.Y));
                scope.Nudge(Z, WorldScale.ToPixels(shift.Z));
                return;
            }

            var yaw = Yaw.GetFloat(itemTime);
            var pitch = Pitch.GetFloat(itemTime);

            var moved = SceneLight.ToDirection(yaw, pitch) * SceneMarker.DirectionalDistance + shift;

            if (moved.LengthSquared() < 1e-8f)
                return;

            var (turned, raised) = SceneLight.ToAngles(Vector3.Normalize(moved));

            scope.Nudge(Yaw, Rotation3D.Wrap(turned - yaw));
            scope.Nudge(Pitch, Math.Clamp(raised, -90f, 90f) - pitch);
        }

        private Vector3 GetPosition(in FrameContext itemTime)
            => new(
                WorldScale.ToWorld(X.GetFloat(itemTime)),
                -WorldScale.ToWorld(Y.GetFloat(itemTime)),
                WorldScale.ToWorld(Z.GetFloat(itemTime)));

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
