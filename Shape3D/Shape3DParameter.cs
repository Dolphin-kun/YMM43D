using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;

namespace Shape3D
{
    internal sealed class Shape3DParameter : ShapeParameter3DBase
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Paint = "色";

        [Display(GroupName = Body, Name = "サイズ")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Size { get; } = new(100, 0, 100000);

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Paint, Name = "塗り", Description = "全体を1色で塗るか、面ごとに色を変えるか")]
        [EnumComboBox]
        public CubeFill Fill { get => fill; set => Set(ref fill, value); }
        private CubeFill fill = CubeFill.Solid;

        [Display(GroupName = Paint, Name = "色", Description = "立方体の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.Solid)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        [Display(GroupName = Paint, Name = "画像", Description = "6面すべてに貼る画像。色は上から掛かります")]
        [FileSelector(FileGroupType.Texture, FileType = FileType.画像)]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.Solid)]
        public string Image { get => image; set => Set(ref image, value); }
        private string image = "";

        [Display(GroupName = Paint, Name = "前面")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public Color FrontColor { get => frontColor; set => Set(ref frontColor, value); }
        private Color frontColor = Gray(0xFF);

        [Display(GroupName = Paint, Name = "前面の画像")]
        [FileSelector(FileGroupType.Texture, FileType = FileType.画像)]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public string FrontImage { get => frontImage; set => Set(ref frontImage, value); }
        private string frontImage = "";

        [Display(GroupName = Paint, Name = "背面")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public Color BackColor { get => backColor; set => Set(ref backColor, value); }
        private Color backColor = Gray(0xA8);

        [Display(GroupName = Paint, Name = "背面の画像")]
        [FileSelector(FileGroupType.Texture, FileType = FileType.画像)]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public string BackImage { get => backImage; set => Set(ref backImage, value); }
        private string backImage = "";

        [Display(GroupName = Paint, Name = "左面")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public Color LeftColor { get => leftColor; set => Set(ref leftColor, value); }
        private Color leftColor = Gray(0xC0);

        [Display(GroupName = Paint, Name = "左面の画像")]
        [FileSelector(FileGroupType.Texture, FileType = FileType.画像)]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public string LeftImage { get => leftImage; set => Set(ref leftImage, value); }
        private string leftImage = "";

        [Display(GroupName = Paint, Name = "右面")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public Color RightColor { get => rightColor; set => Set(ref rightColor, value); }
        private Color rightColor = Gray(0xD8);

        [Display(GroupName = Paint, Name = "右面の画像")]
        [FileSelector(FileGroupType.Texture, FileType = FileType.画像)]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public string RightImage { get => rightImage; set => Set(ref rightImage, value); }
        private string rightImage = "";

        [Display(GroupName = Paint, Name = "上面")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public Color TopColor { get => topColor; set => Set(ref topColor, value); }
        private Color topColor = Gray(0xF0);

        [Display(GroupName = Paint, Name = "上面の画像")]
        [FileSelector(FileGroupType.Texture, FileType = FileType.画像)]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public string TopImage { get => topImage; set => Set(ref topImage, value); }
        private string topImage = "";

        [Display(GroupName = Paint, Name = "下面")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public Color BottomColor { get => bottomColor; set => Set(ref bottomColor, value); }
        private Color bottomColor = Gray(0x90);

        [Display(GroupName = Paint, Name = "下面の画像")]
        [FileSelector(FileGroupType.Texture, FileType = FileType.画像)]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.PerFace)]
        public string BottomImage { get => bottomImage; set => Set(ref bottomImage, value); }
        private string bottomImage = "";

        /// <summary>面の並びは <see cref="CubeFace"/> と同じ順。</summary>
        internal Color[] FaceColors => Fill == CubeFill.Solid
            ? [Color, Color, Color, Color, Color, Color]
            : [FrontColor, BackColor, LeftColor, RightColor, TopColor, BottomColor];

        internal string[] FaceImages => Fill == CubeFill.Solid
            ? [Image, Image, Image, Image, Image, Image]
            : [FrontImage, BackImage, LeftImage, RightImage, TopImage, BottomImage];

        public Shape3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
        }

        public Shape3DParameter() : this(null)
        {
        }

        // 面ごとの初期値は無彩色で、上ほど明るく下ほど暗くしてある。1色だと
        // 立方体が影の無い塊に見えて向きが読めないため。
        private static Color Gray(byte level) => Color.FromRgb(level, level, level);

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices)
            => new Shape3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Size, RotationX, RotationY, RotationZ, CameraSyncAnimation];

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
            // 範囲は本体のプロパティと必ず同じにする。狭いと、引き継ぐときに
            // 値が範囲の端まで丸められて戻ってくる。
            public Animation Size { get; } = new(100, 0, 100000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public CubeFill Fill { get; set; }
            public Color Color { get; set; }
            public Color FrontColor { get; set; }
            public Color BackColor { get; set; }
            public Color LeftColor { get; set; }
            public Color RightColor { get; set; }
            public Color TopColor { get; set; }
            public Color BottomColor { get; set; }
            public string Image { get; set; } = "";
            public string FrontImage { get; set; } = "";
            public string BackImage { get; set; } = "";
            public string LeftImage { get; set; } = "";
            public string RightImage { get; set; } = "";
            public string TopImage { get; set; } = "";
            public string BottomImage { get; set; } = "";

            public SharedData(Shape3DParameter parameter)
            {
                Size.CopyFrom(parameter.Size);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Fill = parameter.Fill;
                Color = parameter.Color;
                FrontColor = parameter.FrontColor;
                BackColor = parameter.BackColor;
                LeftColor = parameter.LeftColor;
                RightColor = parameter.RightColor;
                TopColor = parameter.TopColor;
                BottomColor = parameter.BottomColor;
                Image = parameter.Image;
                FrontImage = parameter.FrontImage;
                BackImage = parameter.BackImage;
                LeftImage = parameter.LeftImage;
                RightImage = parameter.RightImage;
                TopImage = parameter.TopImage;
                BottomImage = parameter.BottomImage;
            }

            public void CopyTo(Shape3DParameter parameter)
            {
                parameter.Size.CopyFrom(Size);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.Fill = Fill;
                parameter.Color = Color;
                parameter.FrontColor = FrontColor;
                parameter.BackColor = BackColor;
                parameter.LeftColor = LeftColor;
                parameter.RightColor = RightColor;
                parameter.TopColor = TopColor;
                parameter.BottomColor = BottomColor;
                parameter.Image = Image;
                parameter.FrontImage = FrontImage;
                parameter.BackImage = BackImage;
                parameter.LeftImage = LeftImage;
                parameter.RightImage = RightImage;
                parameter.TopImage = TopImage;
                parameter.BottomImage = BottomImage;
            }
        }
    }
}
