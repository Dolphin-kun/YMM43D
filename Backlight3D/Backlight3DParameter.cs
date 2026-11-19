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

namespace Backlight3D
{
    public sealed class Backlight3DParameter(SharedDataStore? store) : ShapeParameter3DBase(store), ISceneRotation3D
    {
        public const int LeastSlices = 8;
        public const int MostSlices = 192;

        private const string Air = "";
        private const string Rotation = "3D回転";

        [Display(GroupName = Air, Name = "濃さ", Description = "空気の埃っぽさ")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Density { get; } = new(60, 0, 10000);

        [Display(GroupName = Air, Name = "色", Description = "光の色に掛け合わせる色")]
        [ColorPicker]
        public Color Tint { get; set => Set(ref field, value); } = Colors.White;

        [Display(GroupName = Air, Name = "幅", Description = "空気がある範囲の横の長さ")]
        [AnimationSlider("F0", "px", 100, 4000)]
        public Animation Width { get; } = new(1600, 1, 1000000);

        [Display(GroupName = Air, Name = "高さ", Description = "空気がある範囲の縦の長さ")]
        [AnimationSlider("F0", "px", 100, 4000)]
        public Animation Height { get; } = new(1200, 1, 1000000);

        [Display(GroupName = Air, Name = "奥行き", Description = "空気がある範囲の奥行き")]
        [AnimationSlider("F0", "px", 100, 4000)]
        public Animation Depth { get; } = new(1600, 1, 1000000);

        [Display(GroupName = Air, Name = "ふちのぼかし", Description = "範囲のふちをなだらかに薄くします")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation EdgeBlur { get; } = new(40, 0, 100);

        [Display(GroupName = Air, Name = "前に集める", Description = "光源の方を向いたときだけ強く光らせる割合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Forward { get; } = new(70, 0, 100);

        [Display(GroupName = Air, Name = "にじみ", Description = "光源のまわりのぼうっとした明るさ")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Bloom { get; } = new(80, 0, 10000);

        [Display(GroupName = Air, Name = "描く細かさ", Description = "縮めて描いてから広げて軽くします")]
        [EnumComboBox]
        public BacklightDetail Detail { get; set => Set(ref field, value); } = BacklightDetail.Half;

        [Display(GroupName = Air, Name = "歩数", Description = "空気を何枚に切って描くか")]
        [TextBoxSlider("F0", "", LeastSlices, 128)]
        [Range(LeastSlices, MostSlices)]
        public int Slices { get; set => Set(ref field, Math.Clamp(value, LeastSlices, MostSlices)); } = 40;

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        public Backlight3DParameter() : this(null)
        {
        }

        public Vector3 GetSceneRotation(in FrameContext itemTime)
            => new(RotationX.GetFloat(itemTime), RotationY.GetFloat(itemTime), RotationZ.GetFloat(itemTime));

        internal Vector3 GetSize(in FrameContext itemTime)
            => new Vector3(Width.GetFloat(itemTime), Height.GetFloat(itemTime), Depth.GetFloat(itemTime)) * WorldScale.ToWorld(1f);

        internal Matrix4x4 GetLocalMatrix(in FrameContext itemTime) => Matrix4x4.CreateScale(GetSize(itemTime)) * GetTurn(itemTime);

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices) => new Backlight3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Density, Width, Height, Depth, EdgeBlur, Forward, Bloom, RotationX, RotationY, RotationZ, CameraSyncAnimation, .. PivotAnimatables];

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store) => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store) => store.Save(new SharedData(this));

        private sealed class SharedData
        {
            public Animation Density { get; } = new(60, 0, 10000);
            public Animation Width { get; } = new(1600, 1, 1000000);
            public Animation Height { get; } = new(1200, 1, 1000000);
            public Animation Depth { get; } = new(1600, 1, 1000000);
            public Animation EdgeBlur { get; } = new(40, 0, 100);
            public Animation Forward { get; } = new(70, 0, 100);
            public Animation Bloom { get; } = new(80, 0, 10000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public Color Tint { get; } = Colors.White;
            public int Slices { get; } = 40;
            public BacklightDetail Detail { get; } = BacklightDetail.Half;

            public SharedData()
            {
            }

            public SharedData(Backlight3DParameter from)
            {
                Density.CopyFrom(from.Density);
                Width.CopyFrom(from.Width);
                Height.CopyFrom(from.Height);
                Depth.CopyFrom(from.Depth);
                EdgeBlur.CopyFrom(from.EdgeBlur);
                Forward.CopyFrom(from.Forward);
                Bloom.CopyFrom(from.Bloom);
                RotationX.CopyFrom(from.RotationX);
                RotationY.CopyFrom(from.RotationY);
                RotationZ.CopyFrom(from.RotationZ);
                Tint = from.Tint;
                Slices = from.Slices;
                Detail = from.Detail;
            }

            public void CopyTo(Backlight3DParameter onto)
            {
                onto.Density.CopyFrom(Density);
                onto.Width.CopyFrom(Width);
                onto.Height.CopyFrom(Height);
                onto.Depth.CopyFrom(Depth);
                onto.EdgeBlur.CopyFrom(EdgeBlur);
                onto.Forward.CopyFrom(Forward);
                onto.Bloom.CopyFrom(Bloom);
                onto.RotationX.CopyFrom(RotationX);
                onto.RotationY.CopyFrom(RotationY);
                onto.RotationZ.CopyFrom(RotationZ);
                onto.Tint = Tint;
                onto.Slices = Slices;
                onto.Detail = Detail;
            }
        }
    }
}
