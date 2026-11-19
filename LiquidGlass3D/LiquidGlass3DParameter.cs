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

namespace LiquidGlass3D
{
    public sealed class LiquidGlass3DParameter(SharedDataStore? sharedData) : ShapeParameter3DBase(sharedData), ISceneRotation3D, IGlassLook
    {
        private const string Form = "形";
        private const string Rotation = "3D回転";
        private const string Glass = "ガラス";
        private const string Surface = "表面";

        [Display(GroupName = Form, Name = "形", Description = "ガラスの形")]
        [EnumComboBox]
        public GlassShape Shape { get; set => Set(ref field, value, nameof(Shape), nameof(IsBox), nameof(IsRound), nameof(IsTall), nameof(IsTube)); } = GlassShape.RoundedBox;

        public bool IsBox => Shape == GlassShape.RoundedBox;

        public bool IsRound => Shape != GlassShape.RoundedBox;

        public bool IsTall => Shape == GlassShape.Cylinder;

        public bool IsTube => Shape == GlassShape.Torus;

        [Display(GroupName = Form, Name = "幅", Description = "箱の横の長さ")]
        [AnimationSlider("F1", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsBox), true)]
        public Animation Width { get; } = new(400, 0, 100000);

        [Display(GroupName = Form, Name = "高さ", Description = "箱の縦の長さ")]
        [AnimationSlider("F1", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsBox), true)]
        public Animation Height { get; } = new(240, 0, 100000);

        [Display(GroupName = Form, Name = "厚み", Description = "箱の奥行き")]
        [AnimationSlider("F1", "px", 0, 500)]
        [ShowPropertyEditorWhen(nameof(IsBox), true)]
        public Animation Thickness { get; } = new(60, 0, 100000);

        [Display(GroupName = Form, Name = "角の丸み", Description = "箱の角の半径")]
        [AnimationSlider("F1", "px", 0, 250)]
        [ShowPropertyEditorWhen(nameof(IsBox), true)]
        public Animation CornerRadius { get; } = new(30, 0, 100000);

        [Display(GroupName = Form, Name = "直径", Description = "形の差し渡しの長さ")]
        [AnimationSlider("F1", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsRound), true)]
        public Animation Diameter { get; } = new(300, 0, 100000);

        [Display(GroupName = Form, Name = "長さ", Description = "軸の向きの長さ")]
        [AnimationSlider("F1", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsTall), true)]
        public Animation Length { get; } = new(300, 0, 100000);

        [Display(GroupName = Form, Name = "太さ", Description = "輪の断面の太さ")]
        [AnimationSlider("F1", "px", 0, 500)]
        [ShowPropertyEditorWhen(nameof(IsTube), true)]
        public Animation TubeThickness { get; } = new(90, 0, 100000);

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Glass, Name = "屈折率", Description = "光の曲がり具合で、窓ガラスは約 1.5、水は約 1.33 です")]
        [AnimationSlider("F2", "", 1, 2)]
        public Animation RefractiveIndex { get; } = new(1.5, 1, 4);

        [Display(GroupName = Glass, Name = "向こうまでの距離", Description = "ガラスの向こうの物までのおおよその距離")]
        [AnimationSlider("F0", "px", 0, 1000)]
        public Animation Distance { get; } = new(300, 0, 100000);

        [Display(GroupName = Glass, Name = "すりガラス", Description = "ガラス越しの像のぼけ具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Frost { get; } = new(15, 0, 100);

        [Display(GroupName = Glass, Name = "色収差", Description = "像の色が分かれてにじむ強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Dispersion { get; } = new(15, 0, 1000);

        [Display(GroupName = Glass, Name = "色", Description = "ガラスの色")]
        [ColorPicker]
        public Color TintColor { get => tintColor; set => Set(ref tintColor, value); }
        private Color tintColor = Colors.White;

        [Display(GroupName = Glass, Name = "色の濃さ", Description = "ガラスの色を像に掛ける強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation TintAmount { get; } = new(10, 0, 100);

        [Display(GroupName = Surface, Name = "映り込み", Description = "斜めから見たふちほど強くなる、表面の照り返し")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Reflection { get; } = new(60, 0, 100);

        [Display(GroupName = Surface, Name = "つや", Description = "光源が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Gloss { get; } = new(60, 0, 1000);

        [Display(GroupName = Surface, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation GlossSharpness { get; } = new(80, 0, 100);

        public LiquidGlass3DParameter() : this(null)
        {
        }

        internal Vector3 GetSizePixels(in FrameContext time)
        {
            var across = MathF.Max(Diameter.GetFloat(time), 0f);

            return Shape switch
            {
                GlassShape.Sphere or GlassShape.Octahedron => new Vector3(across),
                GlassShape.Cylinder => new Vector3(across, MathF.Max(Length.GetFloat(time), 0f), across),
                GlassShape.Torus => new Vector3(across, MathF.Max(TubeThickness.GetFloat(time), 0f), across),
                _ => Vector3.Max(new Vector3(Width.GetFloat(time), Height.GetFloat(time), Thickness.GetFloat(time)), Vector3.Zero),
            };
        }

        internal Matrix4x4 GetLocalMatrix(in FrameContext time) => Matrix4x4.CreateScale(GetSizePixels(time) * WorldScale.ToWorld(1f)) * GetTurn(time);

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices) => new LiquidGlass3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Width, Height, Thickness, CornerRadius, Diameter, Length, TubeThickness, RotationX, RotationY, RotationZ, RefractiveIndex, Distance, Frost, Dispersion, TintAmount, Reflection, Gloss, GlossSharpness, CameraSyncAnimation, .. PivotAnimatables];

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store) => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store) => store.Save(new SharedData(this));

        private sealed class SharedData
        {
            public GlassShape Shape { get; set; }
            public Color TintColor { get; set; } = Colors.White;
            public Animation Width { get; } = new(400, 0, 100000);
            public Animation Height { get; } = new(240, 0, 100000);
            public Animation Thickness { get; } = new(60, 0, 100000);
            public Animation CornerRadius { get; } = new(30, 0, 100000);
            public Animation Diameter { get; } = new(300, 0, 100000);
            public Animation Length { get; } = new(300, 0, 100000);
            public Animation TubeThickness { get; } = new(90, 0, 100000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public Animation RefractiveIndex { get; } = new(1.5, 1, 4);
            public Animation Distance { get; } = new(300, 0, 100000);
            public Animation Frost { get; } = new(15, 0, 100);
            public Animation Dispersion { get; } = new(15, 0, 1000);
            public Animation TintAmount { get; } = new(10, 0, 100);
            public Animation Reflection { get; } = new(60, 0, 100);
            public Animation Gloss { get; } = new(60, 0, 1000);
            public Animation GlossSharpness { get; } = new(80, 0, 100);

            public SharedData(LiquidGlass3DParameter parameter)
            {
                Shape = parameter.Shape;
                TintColor = parameter.TintColor;

                foreach (var (shared, source) in Pairs(this, parameter))
                    shared.CopyFrom(source);
            }

            public void CopyTo(LiquidGlass3DParameter parameter)
            {
                parameter.Shape = Shape;
                parameter.TintColor = TintColor;

                foreach (var (shared, target) in Pairs(this, parameter))
                    target.CopyFrom(shared);
            }

            private static (Animation Shared, Animation Parameter)[] Pairs(SharedData shared, LiquidGlass3DParameter parameter) =>
            [
                (shared.Width, parameter.Width), (shared.Height, parameter.Height), (shared.Thickness, parameter.Thickness),
                (shared.CornerRadius, parameter.CornerRadius), (shared.Diameter, parameter.Diameter),
                (shared.Length, parameter.Length), (shared.TubeThickness, parameter.TubeThickness),
                (shared.RotationX, parameter.RotationX), (shared.RotationY, parameter.RotationY), (shared.RotationZ, parameter.RotationZ),
                (shared.RefractiveIndex, parameter.RefractiveIndex), (shared.Distance, parameter.Distance),
                (shared.Frost, parameter.Frost), (shared.Dispersion, parameter.Dispersion), (shared.TintAmount, parameter.TintAmount),
                (shared.Reflection, parameter.Reflection), (shared.Gloss, parameter.Gloss), (shared.GlossSharpness, parameter.GlossSharpness),
            ];
        }
    }
}
