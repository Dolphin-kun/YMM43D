using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace PixelPoints3D
{
    /// <summary>
    /// アイテムの画像を格子状の点の集まりに置き換えるエフェクト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 画像の不透明な部分に一定間隔で点を打ち、隣り合う点を線や面でつなぎます。
    /// 奥行き方向にも層を重ねられるので、平らな絵から立体的な点群を作れます。
    /// </para>
    /// <para>
    /// 画像を CPU に読み戻して走査するのではなく、格子は最初から全面に張り、
    /// 中身があるかどうかはシェーダーが判定して捨てます。こうすると画像が
    /// 動いても作り直しが要らず、点の数が増えても費用が変わりません。
    /// </para>
    /// </remarks>
    [VideoEffect("点群3D", ["3D"], [])]
    public class PixelPoints3DEffect : VideoEffect3DBase
    {
        public override string Label => "点群3D";

        private const string Grid = "点群3D";
        private const string Shape = "点群3D／形";
        private const string Point = "点群3D／点";
        private const string Line = "点群3D／線";
        private const string Face = "点群3D／面";
        private const string Scatter = "点群3D／ばらつき";
        private const string Place = "点群3D／配置";

        [Display(GroupName = Grid, Name = "距離X", Description = "横方向の点と点の間隔")]
        [AnimationSlider("F0", "px", 1, 200)]
        public Animation SpacingX { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "距離Y", Description = "縦方向の点と点の間隔")]
        [AnimationSlider("F0", "px", 1, 200)]
        public Animation SpacingY { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "距離Z", Description = "奥行き方向の点と点の間隔")]
        [AnimationSlider("F0", "px", 1, 200)]
        public Animation SpacingZ { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "奥行き", Description = "点を重ねる奥行きの厚み。0 なら1枚だけ")]
        [AnimationSlider("F0", "px", 0, 500)]
        public Animation Depth { get; } = new(0, 0, 5000);

        [Display(GroupName = Grid, Name = "しきい値", Description = "この不透明度より薄いところには点を打ちません")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Threshold { get; } = new(50, 0, 100);

        // 「形」には切り替えだけを置く。中身は形ごとの組に分ける。
        // 使わない形の設定が並んでいると、どれがどれに効くのか分からなくなる。

        [Display(GroupName = Shape, Name = "点を描く", Description = "格子の各点に四角い粒を描きます")]
        [ToggleSlider]
        public bool DrawPoints
        {
            get => drawPoints;
            set => Set(ref drawPoints, value);
        }
        private bool drawPoints = true;

        [Display(GroupName = Shape, Name = "線を描く", Description = "隣り合う点どうしを線でつなぎます")]
        [ToggleSlider]
        public bool DrawLines
        {
            get => drawLines;
            set => Set(ref drawLines, value);
        }
        private bool drawLines;

        [Display(GroupName = Shape, Name = "面を描く", Description = "隣り合う4点を三角形2枚で埋めます")]
        [ToggleSlider]
        public bool DrawFaces
        {
            get => drawFaces;
            set => Set(ref drawFaces, value);
        }
        private bool drawFaces;

        [Display(GroupName = Point, Name = "大きさ", Description = "粒の一辺の長さ")]
        [AnimationSlider("F1", "px", 0, 50)]
        [ShowPropertyEditorWhen(nameof(DrawPoints), true)]
        public Animation PointSize { get; } = new(4, 0, 500);

        [Display(GroupName = Point, Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(DrawPoints), true)]
        public PointColorSource PointColorSource
        {
            get => pointColorSource;
            set => Set(ref pointColorSource, value);
        }
        private PointColorSource pointColorSource = PointColorSource.Solid;

        [Display(GroupName = Point, Name = "色", Description = "粒の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(DrawPoints), true)]
        public Color PointColor
        {
            get => pointColor;
            set => Set(ref pointColor, value);
        }
        private Color pointColor = Colors.White;

        [Display(GroupName = Line, Name = "太さ", Description = "つなぐ線の太さ")]
        [AnimationSlider("F1", "px", 0, 20)]
        [ShowPropertyEditorWhen(nameof(DrawLines), true)]
        public Animation LineWidth { get; } = new(2, 0, 500);

        [Display(GroupName = Line, Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(DrawLines), true)]
        public PointColorSource LineColorSource
        {
            get => lineColorSource;
            set => Set(ref lineColorSource, value);
        }
        private PointColorSource lineColorSource = PointColorSource.Solid;

        [Display(GroupName = Line, Name = "色", Description = "線の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(DrawLines), true)]
        public Color LineColor
        {
            get => lineColor;
            set => Set(ref lineColor, value);
        }
        private Color lineColor = Colors.White;

        [Display(GroupName = Face, Name = "不透明度", Description = "面だけに掛かる不透明度")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(DrawFaces), true)]
        public Animation FaceOpacity { get; } = new(100, 0, 100);

        [Display(GroupName = Face, Name = "不透明度のばらつき", Description = "面ごとに不透明度をランダムに散らす量")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(DrawFaces), true)]
        public Animation FaceOpacityRandomness { get; } = new(0, 0, 100);

        [Display(GroupName = Face, Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(DrawFaces), true)]
        public PointColorSource FaceColorSource
        {
            get => faceColorSource;
            set => Set(ref faceColorSource, value);
        }
        private PointColorSource faceColorSource = PointColorSource.Image;

        [Display(GroupName = Face, Name = "色", Description = "面の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(DrawFaces), true)]
        public Color FaceColor
        {
            get => faceColor;
            set => Set(ref faceColor, value);
        }
        private Color faceColor = Colors.White;

        [Display(GroupName = Scatter, Name = "位置X", Description = "横方向へ点をばらつかせる量")]
        [AnimationSlider("F0", "px", -500, 500)]
        public Animation ScatterX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "位置Y", Description = "縦方向へ点をばらつかせる量")]
        [AnimationSlider("F0", "px", -500, 500)]
        public Animation ScatterY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "位置Z", Description = "奥行き方向へ点をばらつかせる量")]
        [AnimationSlider("F0", "px", -500, 500)]
        public Animation ScatterZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "シード", Description = "ばらつき方を変えます")]
        [AnimationSlider("F0", "", 0, 100)]
        public Animation Seed { get; } = new(0, 0, 10000);

        [Display(GroupName = Place, Name = "位置X")]
        [AnimationSlider("F0", "px", -500, 500)]
        public Animation PositionX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "位置Y")]
        [AnimationSlider("F0", "px", -500, 500)]
        public Animation PositionY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "位置Z")]
        [AnimationSlider("F0", "px", -500, 500)]
        public Animation PositionZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "大きさ")]
        [AnimationSlider("F1", "%", 0, 400)]
        public Animation Scale { get; } = new(100, 0, 10000);

        [Display(GroupName = Place, Name = "回転X")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationX { get; } = new(0, -36000, 36000);

        [Display(GroupName = Place, Name = "回転Y")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationY { get; } = new(0, -36000, 36000);

        [Display(GroupName = Place, Name = "回転Z")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationZ { get; } = new(0, -36000, 36000);

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new PixelPoints3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
        [
            SpacingX, SpacingY, SpacingZ, Depth, Threshold,
            PointSize, LineWidth, FaceOpacity, FaceOpacityRandomness,
            ScatterX, ScatterY, ScatterZ, Seed,
            PositionX, PositionY, PositionZ, Scale,
            RotationX, RotationY, RotationZ,
            CameraSyncAnimation,
        ];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    /// <summary>点・線・面の色をどこから取るか。</summary>
    public enum PointColorSource
    {
        /// <summary>指定した色で塗ります。</summary>
        [Display(Name = "単色", Description = "指定した色で塗ります")]
        Solid,

        /// <summary>その位置の画像の色を使います。</summary>
        [Display(Name = "画像", Description = "その位置の画像の色を使います")]
        Image,
    }
}
