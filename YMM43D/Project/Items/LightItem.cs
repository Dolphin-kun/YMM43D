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

        private const string Shade = "影";

        private const int FirstOrder = 100;

        [Display(GroupName = Lamp, Name = "種類",
            Description = "平行光は向きだけ、点光源は置いた場所から周り、スポットライトは円錐の中を照らします", Order = FirstOrder)]
        [EnumComboBox]
        public LightKind Kind
        {
            get => kind;
            set
            {
                Set(ref kind, value);
                OnPropertyChanged(nameof(IsSpot));
                OnPropertyChanged(nameof(IsAimed));
                OnPropertyChanged(nameof(IsPlaced));
                OnPropertyChanged(nameof(IsShadowed));
            }
        }
        private LightKind kind = LightKind.Directional;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsSpot => Kind == LightKind.Spot;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsAimed => Kind is LightKind.Directional or LightKind.Spot;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsPlaced => Kind is LightKind.Point or LightKind.Spot;

        [Display(GroupName = Lamp, Name = "水平角",
            Description = "光が来る向き。0 で正面から", Order = FirstOrder + 1)]
        [AnimationSlider("F1", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(IsAimed), true)]
        public Animation Yaw { get; } = new(SceneLighting.DefaultYaw, -100000, 100000);

        [Display(GroupName = Lamp, Name = "垂直角",
            Description = "光が来る高さ。正で上から", Order = FirstOrder + 2)]
        [AnimationSlider("F1", "°", -90, 90)]
        [ShowPropertyEditorWhen(nameof(IsAimed), true)]
        public Animation Pitch { get; } = new(SceneLighting.DefaultPitch, -90, 90);

        [Display(GroupName = Lamp, Name = "X", Description = "光を置く位置。右が正", Order = FirstOrder + 3)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 4)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation Y { get; } = new(-500, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "Z", Description = "手前が正", Order = FirstOrder + 5)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation Z { get; } = new(500, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "届く距離",
            Description = "この距離まで届きます。遠いほど弱くなります", Order = FirstOrder + 6)]
        [AnimationSlider("F1", "px", 100, 5000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation Reach { get; } = new(2000, 1, 1000000);

        [Display(GroupName = Lamp, Name = "広がり",
            Description = "円錐の開き具合。小さいほど細く絞られます", Order = FirstOrder + 7)]
        [AnimationSlider("F1", "°", SceneLight.MinSpread, SceneLight.MaxSpread)]
        [ShowPropertyEditorWhen(nameof(IsSpot), true)]
        public Animation Spread { get; } = new(30, SceneLight.MinSpread, SceneLight.MaxSpread);

        [Display(GroupName = Lamp, Name = "ふちのぼかし",
            Description = "0 でくっきり、100 で中心から外へなだらかに暗くなります", Order = FirstOrder + 8)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsSpot), true)]
        public Animation EdgeBlur { get; } = new(50, 0, 100);

        [Display(GroupName = Lamp, Name = "色", Order = FirstOrder + 9)]
        [ColorPicker]
        public Color LightColor { get => lightColor; set => Set(ref lightColor, value); }
        private Color lightColor = Colors.White;

        [Display(GroupName = Lamp, Name = "明るさ", Order = FirstOrder + 10)]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Brightness { get; } = new(SceneLighting.DefaultBrightness * 100, 0, 10000);

        [Display(GroupName = Shade, Name = "影を落とす",
            Description = "光をさえぎった物の後ろを暗くします。点光源では使えません", Order = 100)]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsAimed), true)]
        public bool CastsShadow
        {
            get => castsShadow;
            set
            {
                Set(ref castsShadow, value);
                OnPropertyChanged(nameof(IsShadowed));
            }
        }
        private bool castsShadow;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsShadowed => CastsShadow && Kind != LightKind.Point;

        [Display(GroupName = Shade, Name = "影の濃さ",
            Description = "100 で光がまったく届かなくなります", Order = 200)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShadowed), true)]
        public Animation ShadowStrength { get; } = new(80, 0, 100);

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
            var color = LightColor.ToVector3() * (Brightness.GetFloat(itemTime) / 100f);

            if (Kind == LightKind.Spot)
            {
                return WithShade(SceneLight.Spot(
                    GetPosition(itemTime),
                    GetShines(itemTime),
                    color,
                    WorldScale.ToWorld(Reach.GetFloat(itemTime)),
                    Spread.GetFloat(itemTime),
                    EdgeBlur.GetFloat(itemTime) / 100f), itemTime);
            }

            if (Kind == LightKind.Point)
            {
                return SceneLight.Point(
                    GetPosition(itemTime), color, WorldScale.ToWorld(Reach.GetFloat(itemTime)));
            }

            return WithShade(
                SceneLight.FromAngles(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime), color), itemTime);
        }

        private SceneLight WithShade(in SceneLight light, in FrameContext itemTime)
            => CastsShadow ? light.WithShadow(ShadowStrength.GetFloat(itemTime) / 100f) : light;

        public SceneMarker GetMarker(in FrameContext itemTime) => Kind switch
        {
            LightKind.Spot => SceneMarker.ForSpotLight(
                GetPosition(itemTime),
                GetShines(itemTime),
                WorldScale.ToWorld(Reach.GetFloat(itemTime)),
                Spread.GetFloat(itemTime)),

            LightKind.Point => SceneMarker.ForPointLight(
                GetPosition(itemTime), WorldScale.ToWorld(Reach.GetFloat(itemTime))),

            _ => SceneMarker.ForDirectionalLight(
                SceneLight.ToDirection(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime))),
        };

        public void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope)
        {
            if (Kind is LightKind.Point or LightKind.Spot)
            {
                scope.NudgePosition(X, Y, Z, shift);
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
            => WorldScale.ToWorldPosition(X.GetFloat(itemTime), Y.GetFloat(itemTime), Z.GetFloat(itemTime));

        private Vector3 GetShines(in FrameContext itemTime)
            => -SceneLight.ToDirection(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Yaw, Pitch, X, Y, Z, Reach, Spread, EdgeBlur, Brightness, ShadowStrength];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription)
            => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }
}
