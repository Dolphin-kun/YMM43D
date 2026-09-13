using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Model
{
    internal sealed class Model3DParameter : ShapeParameter3DBase
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Paint = "色";

        [Display(GroupName = Body, Name = "ファイル", Description = "obj / gltf / glb のファイル")]
        [FileSelector(FileGroupType.None, CustomFilterName = "3Dモデル", CustomFilterValue = "*.obj;*.gltf;*.glb")]
        public string File { get => file; set => Set(ref file, value ?? string.Empty); }
        private string file = string.Empty;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡し")]
        [AnimationSlider("F1", "px", 0, 1000)]
        public Animation Size { get; } = new(300, 0, 100000);

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Paint, Name = "色", Description = "モデルの色に掛け合わせます。白ならそのまま")]
        [ColorPicker]
        public Color Tint { get => tint; set => Set(ref tint, value); }
        private Color tint = Colors.White;

        [Display(GroupName = Paint, Name = "陰影をつけない",
            Description = "光源を無視して、モデルの色のまま塗ります")]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        [Display(GroupName = Paint, Name = "つや",
            Description = "光が映り込んだ明るい点の強さ。0 でつやなし")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Paint, Name = "つやの鋭さ",
            Description = "大きいほど映り込みが小さく締まり、磨いたように見えます")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        public Model3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
        }

        public Model3DParameter() : this(null)
        {
        }

        internal ModelData? Model => ModelLibrary.Find(File);

        internal SurfaceGloss GetGloss(in FrameContext time)
            => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        internal Matrix4x4 GetLocalMatrix(ModelData model, in FrameContext time)
        {
            var bounds = model.Bounds;
            var extent = bounds.Max - bounds.Min;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));

            var scale = longest > 1e-9f ? WorldScale.ToWorld(Size.GetFloat(time)) / longest : 1f;

            return Matrix4x4.CreateTranslation(-bounds.Center)
                 * Matrix4x4.CreateScale(scale)
                 * Rotation3D.ForObject(RotationX.GetFloat(time), RotationY.GetFloat(time), RotationZ.GetFloat(time));
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices)
            => new Model3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Size, RotationX, RotationY, RotationZ, Gloss, GlossSharpness, CameraSyncAnimation];

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
            public string File { get; set; } = string.Empty;
            public Animation Size { get; } = new(300, 0, 100000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public Animation Gloss { get; } = new(0, 0, 1000);
            public Animation GlossSharpness { get; } = new(50, 0, 100);
            public Color Tint { get; set; } = Colors.White;
            public bool IsUnlit { get; set; }

            public SharedData(Model3DParameter parameter)
            {
                File = parameter.File;
                Size.CopyFrom(parameter.Size);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Gloss.CopyFrom(parameter.Gloss);
                GlossSharpness.CopyFrom(parameter.GlossSharpness);
                Tint = parameter.Tint;
                IsUnlit = parameter.IsUnlit;
            }

            public void CopyTo(Model3DParameter parameter)
            {
                parameter.File = File;
                parameter.Size.CopyFrom(Size);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.Gloss.CopyFrom(Gloss);
                parameter.GlossSharpness.CopyFrom(GlossSharpness);
                parameter.Tint = Tint;
                parameter.IsUnlit = IsUnlit;
            }
        }
    }
}
