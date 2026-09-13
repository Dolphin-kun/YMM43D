using System.ComponentModel;
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
    public sealed class BeamLight3DParameter : ShapeParameter3DBase, IPlacedSceneLightSource
    {
        private const string Beam = "";
        private const string Rotation = "3D回転";
        private const string Lamp = "明かり";

        [Display(GroupName = Beam, Name = "長さ",
            Description = "先端から、光が届かなくなるところまで", Order = 100)]
        [AnimationSlider("F1", "px", 50, 2000)]
        public Animation Length { get; } = new(500, 1, 1000000);

        [Display(GroupName = Beam, Name = "広がり",
            Description = "円錐の開き具合。小さいほど細く絞られます", Order = 200)]
        [AnimationSlider("F1", "°", SceneLight.MinSpread, 60)]
        public Animation Spread { get; } = new(12, SceneLight.MinSpread, SceneLight.MaxSpread);

        [Display(GroupName = Beam, Name = "濃さ",
            Description = "光の筋そのものの見え方。空気の埃っぽさだと思ってください", Order = 300)]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Density { get; } = new(60, 0, 10000);

        [Display(GroupName = Beam, Name = "先の弱まり",
            Description = "0 で先まで同じ濃さ、100 で根元だけ濃くなります", Order = 400)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Decay { get; } = new(50, 0, 100);

        [Display(GroupName = Beam, Name = "ふちのぼかし",
            Description = "0 で境目がはっきり、100 で外へなだらかに薄くなります", Order = 500)]
        [TextBoxSlider("F0", "%", 0, 100)]
        [Range(0, 100)]
        public int EdgeBlur
        {
            get => edgeBlur;
            set => Set(ref edgeBlur, Math.Clamp(value, 0, 100));
        }
        private int edgeBlur = 60;

        [Display(GroupName = Beam, Name = "色", Order = 600)]
        [ColorPicker]
        public Color BeamColor { get => beamColor; set => Set(ref beamColor, value); }
        private Color beamColor = Colors.White;

        [Display(GroupName = Beam, Name = "分割の細かさ",
            Description = "円錐を何枚の面で作るか。大きいほど滑らかで、そのぶん重くなります", Order = 700)]
        [TextBoxSlider("F0", "", BeamShape.MinDetail, BeamShape.MaxDetail)]
        [Range(BeamShape.MinDetail, BeamShape.MaxDetail)]
        public int Detail
        {
            get => detail;
            set => Set(ref detail, Math.Clamp(value, BeamShape.MinDetail, BeamShape.MaxDetail));
        }
        private int detail = 48;

        [Display(GroupName = Rotation, Name = "X",
            Description = "何も回さないと真下を向きます", Order = 100)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y", Order = 200)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z", Order = 300)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Lamp, Name = "まわりを照らす",
            Description = "光の筋だけでなく、当たった物も明るくします", Order = 100)]
        [ToggleSlider]
        public bool IsLightEnabled
        {
            get => isLightEnabled;
            set
            {
                Set(ref isLightEnabled, value);
                OnPropertyChanged(nameof(IsLit));
                OnPropertyChanged(nameof(IsShadowed));
            }
        }
        private bool isLightEnabled = true;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsLit => IsLightEnabled;

        [Display(GroupName = Lamp, Name = "明るさ",
            Description = "当たった物をどれだけ明るくするか", Order = 200)]
        [AnimationSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsLit), true)]
        public Animation Brightness { get; } = new(80, 0, 10000);

        [Display(GroupName = Lamp, Name = "影を落とす",
            Description = "光をさえぎった物の後ろを暗くします", Order = 300)]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsLit), true)]
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
        public bool IsShadowed => IsLightEnabled && CastsShadow;

        [Display(GroupName = Lamp, Name = "影の濃さ",
            Description = "100 で光がまったく届かなくなります", Order = 400)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShadowed), true)]
        public Animation ShadowStrength { get; } = new(80, 0, 100);

        [Display(GroupName = Lamp, Name = "影のぼかし",
            Description = "0 でくっきり、大きいほど影のふちがやわらかくなります", Order = 500)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShadowed), true)]
        public Animation ShadowSoftness { get; } = new(20, 0, 100);

        public BeamLight3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
        }

        public BeamLight3DParameter() : this(null)
        {
        }

        internal float GetLength(in FrameContext itemTime)
            => WorldScale.ToWorld(MathF.Max(Length.GetFloat(itemTime), 1f));

        internal float GetSpread(in FrameContext itemTime)
            => Math.Clamp(Spread.GetFloat(itemTime), SceneLight.MinSpread, SceneLight.MaxSpread);

        internal Matrix4x4 GetOrientation(in FrameContext itemTime)
            => Matrix4x4.CreateRotationX(MathF.PI / 2f)
             * Rotation3D.ForObject(
                   RotationX.GetFloat(itemTime),
                   RotationY.GetFloat(itemTime),
                   RotationZ.GetFloat(itemTime));

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

            var light = SceneLight.Spot(
                Vector3.Transform(Vector3.Zero, aimed),
                axis,
                BeamColor.ToVector3() * (Brightness.GetFloat(itemTime) / 100f),
                GetLength(itemTime) * (float.IsFinite(stretch) && stretch > 0f ? stretch : 1f),
                GetSpread(itemTime),
                EdgeBlur / 100f);

            return CastsShadow
                ? light.WithShadow(ShadowStrength.GetFloat(itemTime) / 100f, ShadowSoftness.GetFloat(itemTime) / 100f)
                : light;
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices)
            => new BeamLight3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Length, Spread, Density, Decay, RotationX, RotationY, RotationZ,
                Brightness, ShadowStrength, ShadowSoftness, CameraSyncAnimation];

        public override IEnumerable<string> CreateMaskExoFilter(
            int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(
            int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store)
            => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store)
            => store.Save(new SharedData(this));

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
