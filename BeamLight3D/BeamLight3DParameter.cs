using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace BeamLight3D
{
    public sealed class BeamLight3DParameter(SharedDataStore? sharedData) : ShapeParameter3DBase(sharedData), IPlacedSceneLightSource, ISceneRotation3D
    {
        private const string Beam = "";
        private const string Rotation = "3D回転";
        private const string Lamp = "明かり";

        [Display(GroupName = Beam, Name = "長さ", Description = "先端から光が届かなくなるところまでの長さ")]
        [AnimationSlider("F1", "px", 50, 2000)]
        public Animation Length { get; } = new(500, 1, 1000000);

        [Display(GroupName = Beam, Name = "広がり", Description = "円錐の開き具合")]
        [AnimationSlider("F1", "°", SceneLight.MinSpread, 60)]
        public Animation Spread { get; } = new(12, SceneLight.MinSpread, SceneLight.MaxSpread);

        [Display(GroupName = Beam, Name = "濃さ", Description = "光の筋が見える空気の埃っぽさ")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Density { get; } = new(60, 0, 10000);

        [Display(GroupName = Beam, Name = "先の弱まり", Description = "根元から先へ向かって光を弱める度合い")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Decay { get; } = new(50, 0, 100);

        [Display(GroupName = Beam, Name = "ふちのぼかし", Description = "円錐のふちを外へなだらかに薄くします")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [Range(0, 100)]
        public int EdgeBlur { get; set => Set(ref field, Math.Clamp(value, 0, 100)); } = 60;

        [Display(GroupName = Beam, Name = "色", Description = "光の色")]
        [ColorPicker]
        public Color BeamColor { get => beamColor; set => Set(ref beamColor, value); }
        private Color beamColor = Colors.White;

        [Display(GroupName = Beam, Name = "分割の細かさ", Description = "円錐を何枚の面で作るか")]
        [TextBoxSlider("F0", "", BeamShape.MinDetail, BeamShape.MaxDetail)]
        [Range(BeamShape.MinDetail, BeamShape.MaxDetail)]
        public int Detail { get; set => Set(ref field, Math.Clamp(value, BeamShape.MinDetail, BeamShape.MaxDetail)); } = 48;

        [Display(GroupName = Rotation, Name = "X", Description = "何も回さないと真下を向きます")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Lamp, Name = "まわりを照らす", Description = "光の筋だけでなく、当たった物も明るくします")]
        [ToggleSlider]
        public bool IsLightEnabled { get; set => Set(ref field, value, nameof(IsLightEnabled), nameof(IsShadowed)); } = true;

        [Display(GroupName = Lamp, Name = "明るさ", Description = "当たった物をどれだけ明るくするか")]
        [AnimationSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsLightEnabled), true)]
        public Animation Brightness { get; } = new(80, 0, 10000);

        [Display(GroupName = Lamp, Name = "影を落とす", Description = "光をさえぎった物の後ろを暗くします")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsLightEnabled), true)]
        public bool CastsShadow { get; set => Set(ref field, value, nameof(CastsShadow), nameof(IsShadowed)); }

        public bool IsShadowed => IsLightEnabled && CastsShadow;

        [Display(GroupName = Lamp, Name = "影の濃さ", Description = "影になったところの暗さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShadowed), true)]
        public Animation ShadowStrength { get; } = new(80, 0, 100);

        [Display(GroupName = Lamp, Name = "影のぼかし", Description = "影のふちのやわらかさ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShadowed), true)]
        public Animation ShadowSoftness { get; } = new(20, 0, 100);

        public BeamLight3DParameter() : this(null)
        {
        }

        internal float GetLength(in FrameContext itemTime) => WorldScale.ToWorld(MathF.Max(Length.GetFloat(itemTime), 1f));

        internal float GetSpread(in FrameContext itemTime) => Math.Clamp(Spread.GetFloat(itemTime), SceneLight.MinSpread, SceneLight.MaxSpread);

        internal Matrix4x4 GetOrientation(in FrameContext itemTime)
        {
            var aim = Rotation3D.ForObject(RotationX.GetFloat(itemTime), RotationY.GetFloat(itemTime), RotationZ.GetFloat(itemTime));

            return GetTurn(itemTime, Matrix4x4.CreateRotationX(MathF.PI / 2f) * aim);
        }

        internal Matrix4x4 GetLocalMatrix(in FrameContext itemTime)
        {
            var length = GetLength(itemTime);
            var radius = length * MathF.Tan(float.DegreesToRadians(GetSpread(itemTime)));

            return Matrix4x4.CreateScale(radius, radius, length) * GetOrientation(itemTime);
        }

        public SceneLight GetLight(in FrameContext itemTime, in Matrix4x4 placement)
        {
            var aimed = GetOrientation(itemTime) * placement;
            var axis = Vector3.TransformNormal(Vector3.UnitZ, aimed);
            var stretch = axis.Length();

            var color = BeamColor.ToVector3() * (Brightness.GetFloat(itemTime) / 100f);
            var reach = GetLength(itemTime) * (float.IsFinite(stretch) && stretch > 0f ? stretch : 1f);

            var light = SceneLight.Spot(Vector3.Transform(Vector3.Zero, aimed), axis, color, reach, GetSpread(itemTime), EdgeBlur / 100f);

            return CastsShadow ? light.WithShadow(ShadowStrength.GetFloat(itemTime) / 100f, ShadowSoftness.GetFloat(itemTime) / 100f) : light;
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices) => new BeamLight3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Length, Spread, Density, Decay, RotationX, RotationY, RotationZ, Brightness, ShadowStrength, ShadowSoftness, CameraSyncAnimation, .. PivotAnimatables];

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store) => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store) => store.Save(new SharedData(this));

        private sealed class SharedData
        {
            public Animation Length { get; } = new(500, 1, 1000000);
            public Animation Spread { get; } = new(12, SceneLight.MinSpread, SceneLight.MaxSpread);
            public Animation Density { get; } = new(60, 0, 10000);
            public Animation Decay { get; } = new(50, 0, 100);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public Animation Brightness { get; } = new(80, 0, 10000);
            public Animation ShadowStrength { get; } = new(80, 0, 100);
            public Animation ShadowSoftness { get; } = new(20, 0, 100);
            public int EdgeBlur { get; set; }
            public int Detail { get; set; }
            public Color BeamColor { get; set; }
            public bool IsLightEnabled { get; set; }
            public bool CastsShadow { get; set; }

            public SharedData(BeamLight3DParameter parameter)
            {
                Length.CopyFrom(parameter.Length);
                Spread.CopyFrom(parameter.Spread);
                Density.CopyFrom(parameter.Density);
                Decay.CopyFrom(parameter.Decay);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Brightness.CopyFrom(parameter.Brightness);
                ShadowStrength.CopyFrom(parameter.ShadowStrength);
                ShadowSoftness.CopyFrom(parameter.ShadowSoftness);
                EdgeBlur = parameter.EdgeBlur;
                Detail = parameter.Detail;
                BeamColor = parameter.BeamColor;
                IsLightEnabled = parameter.IsLightEnabled;
                CastsShadow = parameter.CastsShadow;
            }

            public void CopyTo(BeamLight3DParameter parameter)
            {
                parameter.Length.CopyFrom(Length);
                parameter.Spread.CopyFrom(Spread);
                parameter.Density.CopyFrom(Density);
                parameter.Decay.CopyFrom(Decay);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.Brightness.CopyFrom(Brightness);
                parameter.ShadowStrength.CopyFrom(ShadowStrength);
                parameter.ShadowSoftness.CopyFrom(ShadowSoftness);
                parameter.EdgeBlur = EdgeBlur;
                parameter.Detail = Detail;
                parameter.BeamColor = BeamColor;
                parameter.IsLightEnabled = IsLightEnabled;
                parameter.CastsShadow = CastsShadow;
            }
        }
    }
}
