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

        private const int AfterGeneral = 100;

        [Display(GroupName = Lamp, Name = "種類", Description = "平行光は向きだけ、点光源は置いた場所から周り、スポットライトは円錐の中を照らします", Order = AfterGeneral)]
        [EnumComboBox]
        public LightKind Kind { get; set => Set(ref field, value, nameof(Kind), nameof(IsSpot), nameof(IsAimed), nameof(IsPlaced)); } = LightKind.Directional;

        public bool IsSpot => Kind == LightKind.Spot;

        public bool IsAimed => Kind is LightKind.Directional or LightKind.Spot;

        public bool IsPlaced => Kind is LightKind.Point or LightKind.Spot;

        [Display(GroupName = Lamp, Name = "水平角", Description = "光が来る左右の向き", Order = AfterGeneral)]
        [AnimationSlider("F1", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(IsAimed), true)]
        public Animation Yaw { get; } = new(SceneLighting.DefaultYaw, -100000, 100000);

        [Display(GroupName = Lamp, Name = "垂直角", Description = "光が来る上下の向き", Order = AfterGeneral)]
        [AnimationSlider("F1", "°", -90, 90)]
        [ShowPropertyEditorWhen(nameof(IsAimed), true)]
        public Animation Pitch { get; } = new(SceneLighting.DefaultPitch, -90, 90);

        [Display(GroupName = Lamp, Name = "X", Description = "光を置く横の位置で、右が正です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "Y", Description = "光を置く縦の位置で、下が正です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation Y { get; } = new(-500, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "Z", Description = "光を置く奥行きの位置で、手前が正です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation Z { get; } = new(500, -1000000, 1000000);

        [Display(GroupName = Lamp, Name = "届く距離", Description = "光が届く距離", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", 100, 5000)]
        [ShowPropertyEditorWhen(nameof(IsPlaced), true)]
        public Animation Reach { get; } = new(2000, 1, 1000000);

        [Display(GroupName = Lamp, Name = "広がり", Description = "円錐の開き具合", Order = AfterGeneral)]
        [AnimationSlider("F1", "°", SceneLight.MinSpread, SceneLight.MaxSpread)]
        [ShowPropertyEditorWhen(nameof(IsSpot), true)]
        public Animation Spread { get; } = new(30, SceneLight.MinSpread, SceneLight.MaxSpread);

        [Display(GroupName = Lamp, Name = "ふちのぼかし", Description = "円錐のふちを外へなだらかに暗くします", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsSpot), true)]
        public Animation EdgeBlur { get; } = new(50, 0, 100);

        [Display(GroupName = Lamp, Name = "色", Description = "光の色", Order = AfterGeneral)]
        [ColorPicker]
        public Color LightColor { get => lightColor; set => Set(ref lightColor, value); }
        private Color lightColor = Colors.White;

        [Display(GroupName = Lamp, Name = "明るさ", Description = "光の明るさ", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Brightness { get; } = new(SceneLighting.DefaultBrightness * 100, 0, 10000);

        [Display(GroupName = Shade, Name = "影を落とす", Description = "光をさえぎった物の後ろを暗くします", Order = AfterGeneral)]
        [ToggleSlider]
        public bool CastsShadow { get; set => Set(ref field, value); }

        [Display(GroupName = Shade, Name = "影の濃さ", Description = "影になったところの暗さ", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(CastsShadow), true)]
        public Animation ShadowStrength { get; } = new(80, 0, 100);

        [Display(GroupName = Shade, Name = "影のぼかし", Description = "影のふちのやわらかさ", Order = AfterGeneral)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(CastsShadow), true)]
        public Animation ShadowSoftness { get; } = new(20, 0, 100);

        public override string Label => "3D光源";

        public override Color ItemColor { get => itemColor; set => Set(ref itemColor, value); }
        private Color itemColor = Color.FromRgb(0xFF, 0xD5, 0x4F);

        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        public SceneLight GetLight(in FrameContext itemTime)
        {
            var color = LightColor.ToVector3() * (Brightness.GetFloat(itemTime) / 100f);

            if (Kind == LightKind.Spot)
            {
                var spot = SceneLight.Spot(GetPosition(itemTime), GetShines(itemTime), color, GetReach(itemTime), Spread.GetFloat(itemTime), EdgeBlur.GetFloat(itemTime) / 100f);

                return WithShade(spot, itemTime);
            }

            if (Kind == LightKind.Point)
                return SceneLight.Point(GetPosition(itemTime), color, GetReach(itemTime));

            return WithShade(SceneLight.FromAngles(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime), color), itemTime);
        }

        private SceneLight WithShade(in SceneLight light, in FrameContext itemTime) => CastsShadow ? light.WithShadow(ShadowStrength.GetFloat(itemTime) / 100f, ShadowSoftness.GetFloat(itemTime) / 100f) : light;

        public SceneMarker GetMarker(in FrameContext itemTime) => Kind switch
        {
            LightKind.Spot => SceneMarker.ForSpotLight(GetPosition(itemTime), GetShines(itemTime), GetReach(itemTime), Spread.GetFloat(itemTime)),
            LightKind.Point => SceneMarker.ForPointLight(GetPosition(itemTime), GetReach(itemTime)),
            _ => SceneMarker.ForDirectionalLight(SceneLight.ToDirection(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime))),
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

        private Vector3 GetPosition(in FrameContext itemTime) => WorldScale.ToWorldPosition(X.GetFloat(itemTime), Y.GetFloat(itemTime), Z.GetFloat(itemTime));

        private Vector3 GetShines(in FrameContext itemTime) => -SceneLight.ToDirection(Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime));

        private float GetReach(in FrameContext itemTime) => WorldScale.ToWorld(Reach.GetFloat(itemTime));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Yaw, Pitch, X, Y, Z, Reach, Spread, EdgeBlur, Brightness, ShadowStrength, ShadowSoftness];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription) => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }
}
