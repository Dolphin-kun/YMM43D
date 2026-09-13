using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace LiquidGlass3D
{
    public enum GlassShape
    {
        [Display(Name = "角丸の箱")]
        RoundedBox,

        [Display(Name = "楕円体")]
        Ellipsoid,
    }

    public sealed class LiquidGlass3DParameter : ShapeParameter3DBase
    {
        private const string Form = "形";
        private const string Rotation = "3D回転";
        private const string Glass = "ガラス";
        private const string Surface = "表面";

        [Display(GroupName = Form, Name = "形", Order = 100)]
        [EnumComboBox]
        public GlassShape Shape { get => shape; set => Set(ref shape, value); }
        private GlassShape shape = GlassShape.RoundedBox;

        [Display(GroupName = Form, Name = "幅", Order = 200)]
        [AnimationSlider("F1", "px", 0, 1000)]
        public Animation Width { get; } = new(400, 0, 100000);

        [Display(GroupName = Form, Name = "高さ", Order = 300)]
        [AnimationSlider("F1", "px", 0, 1000)]
        public Animation Height { get; } = new(240, 0, 100000);

        [Display(GroupName = Form, Name = "厚み", Order = 400)]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Thickness { get; } = new(60, 0, 100000);

        [Display(GroupName = Form, Name = "角の丸み",
            Description = "角丸の箱の角の半径。いちばん短い辺の半分で、ふちが完全に丸くなります", Order = 500)]
        [AnimationSlider("F1", "px", 0, 250)]
        public Animation CornerRadius { get; } = new(30, 0, 100000);

        [Display(GroupName = Rotation, Name = "X", Order = 100)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y", Order = 200)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z", Order = 300)]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Glass, Name = "屈折率",
            Description = "1 で曲がらず、大きいほど強く曲がります。窓ガラスは約 1.5、水は約 1.33", Order = 100)]
        [AnimationSlider("F2", "", 1, 2)]
        public Animation RefractiveIndex { get; } = new(1.5, 1, 4);

        [Display(GroupName = Glass, Name = "向こうまでの距離",
            Description = "ガラスの向こうの物までのおおよその距離。大きいほど、ふちで像が大きくずれます", Order = 200)]
        [AnimationSlider("F0", "px", 0, 1000)]
        public Animation Distance { get; } = new(300, 0, 100000);

        [Display(GroupName = Glass, Name = "すりガラス", Description = "ガラス越しの像のぼけ具合", Order = 300)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Frost { get; } = new(15, 0, 100);

        [Display(GroupName = Glass, Name = "色収差", Description = "像の色が分かれてにじむ強さ", Order = 400)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Dispersion { get; } = new(15, 0, 1000);

        [Display(GroupName = Glass, Name = "色", Order = 500)]
        [ColorPicker]
        public Color TintColor { get => tintColor; set => Set(ref tintColor, value); }
        private Color tintColor = Colors.White;

        [Display(GroupName = Glass, Name = "色の濃さ", Description = "ガラスの色を像に掛ける強さ", Order = 600)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation TintAmount { get; } = new(10, 0, 100);

        [Display(GroupName = Surface, Name = "映り込み",
            Description = "斜めから見たふちほど強くなる、表面の照り返し", Order = 100)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Reflection { get; } = new(60, 0, 100);

        [Display(GroupName = Surface, Name = "つや", Description = "光源が映り込んだ明るい点の強さ", Order = 200)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Gloss { get; } = new(60, 0, 1000);

        [Display(GroupName = Surface, Name = "つやの鋭さ", Order = 300)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation GlossSharpness { get; } = new(80, 0, 100);

        public LiquidGlass3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
        }

        public LiquidGlass3DParameter() : this(null)
        {
        }

        internal Vector3 GetSizePixels(in FrameContext time)
            => new(
                MathF.Max(Width.GetFloat(time), 0f),
                MathF.Max(Height.GetFloat(time), 0f),
                MathF.Max(Thickness.GetFloat(time), 0f));

        internal Matrix4x4 GetLocalMatrix(in FrameContext time)
        {
            var size = GetSizePixels(time);

            return Matrix4x4.CreateScale(WorldScale.ToWorld(size.X), WorldScale.ToWorld(size.Y), WorldScale.ToWorld(size.Z))
                 * Rotation3D.ForObject(RotationX.GetFloat(time), RotationY.GetFloat(time), RotationZ.GetFloat(time));
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices)
            => new LiquidGlass3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Width, Height, Thickness, CornerRadius, RotationX, RotationY, RotationZ, RefractiveIndex, Distance,
                Frost, Dispersion, TintAmount, Reflection, Gloss, GlossSharpness, CameraSyncAnimation];

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
            public GlassShape Shape { get; set; }
            public Color TintColor { get; set; } = Colors.White;
            public Animation Width { get; } = new(400, 0, 100000);
            public Animation Height { get; } = new(240, 0, 100000);
            public Animation Thickness { get; } = new(60, 0, 100000);
            public Animation CornerRadius { get; } = new(30, 0, 100000);
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
                (shared.CornerRadius, parameter.CornerRadius),
                (shared.RotationX, parameter.RotationX), (shared.RotationY, parameter.RotationY), (shared.RotationZ, parameter.RotationZ),
                (shared.RefractiveIndex, parameter.RefractiveIndex), (shared.Distance, parameter.Distance),
                (shared.Frost, parameter.Frost), (shared.Dispersion, parameter.Dispersion), (shared.TintAmount, parameter.TintAmount),
                (shared.Reflection, parameter.Reflection), (shared.Gloss, parameter.Gloss), (shared.GlossSharpness, parameter.GlossSharpness),
            ];
        }
    }
}
