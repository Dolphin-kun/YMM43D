using System.Collections.Immutable;
using System.ComponentModel;
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
        private const string Shape = "形";
        private const string Deform = "変形";
        private const string Scatter = "ばらつき";
        private const string Place = "配置";

        [Display(GroupName = Grid, Name = "距離X", Description = "X軸方向の点と点の間隔")]
        [AnimationSlider("F0", "px", 1, 200)]
        public Animation SpacingX { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "距離Y", Description = "Y軸方向の点と点の間隔")]
        [AnimationSlider("F0", "px", 1, 200)]
        public Animation SpacingY { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "距離Z", Description = "z軸方向の点と点の間隔")]
        [AnimationSlider("F0", "px", 1, 200)]
        public Animation SpacingZ { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "奥行き", Description = "点を重ねる奥行きの厚み。0 なら1枚だけ")]
        [AnimationSlider("F0", "px", 0, 500)]
        public Animation Depth { get; } = new(0, 0, 5000);

        [Display(GroupName = Grid, Name = "しきい値", Description = "この不透明度より薄いところには点を打ちません")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Threshold { get; } = new(50, 0, 100);

        // 形
        [Display(GroupName = Shape, Name = "点を描く", Description = "格子の各点に四角い粒を描きます")]
        [ToggleSlider]
        public bool DrawPoints { get => drawPoints; set => Set(ref drawPoints, value); }
        private bool drawPoints = true;

        [Display(GroupName = Shape, Name = "線を描く", Description = "隣り合う点どうしを線でつなぎます")]
        [ToggleSlider]
        public bool DrawLines { get => drawLines; set => Set(ref drawLines, value); }
        private bool drawLines;

        [Display(GroupName = Shape, Name = "面を描く", Description = "隣り合う4点を三角形2枚で埋めます")]
        [ToggleSlider]
        public bool DrawFaces { get => drawFaces; set => Set(ref drawFaces, value); }
        private bool drawFaces;

        // 点・線・面のパラメータ（AutoGenerateField で表示制御）
        [Display(AutoGenerateField = true)]
        public ImmutableList<PointParams> PointSettings { get => pointSettings; set => Set(ref pointSettings, value); }
        private ImmutableList<PointParams> pointSettings = [new PointParams()];

        [Display(AutoGenerateField = true)]
        public ImmutableList<LineParams> LineSettings { get => lineSettings; set => Set(ref lineSettings, value); }
        private ImmutableList<LineParams> lineSettings = [];

        [Display(AutoGenerateField = true)]
        public ImmutableList<FaceParams> FaceSettings { get => faceSettings; set => Set(ref faceSettings, value); }
        private ImmutableList<FaceParams> faceSettings = [];

        // 変形
        [Display(GroupName = Deform, Name = "種類", Description = "点の並びをどう歪ませるか")]
        [EnumComboBox]
        public DeformKind DeformKind
        {
            get => deformKind;
            set
            {
                Set(ref deformKind, value);
                OnPropertyChanged(nameof(IsDeformed));
            }
        }
        private DeformKind deformKind = DeformKind.None;

        /// <summary>変形の設定を表示してよいか。</summary>
        /// <remarks>
        /// <c>ShowPropertyEditorWhen</c> は「この値のとき」しか書けないため、
        /// 「なし以外のとき」を表せません。判定をこちらに寄せて、真偽で答えます。
        /// </remarks>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsDeformed => DeformKind != DeformKind.None;

        [Display(GroupName = Deform, Name = "強さ", Description = "歪ませる量。負にすると逆向きに歪みます")]
        [AnimationSlider("F0", "%", -100, 100)]
        [ShowPropertyEditorWhen(nameof(IsDeformed), true)]
        public Animation DeformAmount { get; } = new(50, -100000, 100000);

        [Display(GroupName = Deform, Name = "向き", Description = "変形の軸。波が進む向き、ねじれの回転軸、球の極になります")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsDeformed), true)]
        public DeformAxis DeformAxis { get => deformAxis; set => Set(ref deformAxis, value); }
        private DeformAxis deformAxis = DeformAxis.Y;

        [Display(GroupName = Deform, Name = "周期", Description = "波1つ分の長さ")]
        [AnimationSlider("F0", "px", 10, 1000)]
        [ShowPropertyEditorWhen(nameof(DeformKind), DeformKind.Wave)]
        public Animation DeformPeriod { get; } = new(200, 1, 100000);

        [Display(GroupName = Deform, Name = "位相", Description = "波やねじれのずれ。動かすと流れます")]
        [AnimationSlider("F0", "°", -360, 360)]
        [ShowPropertyEditorWhen(nameof(IsDeformed), true)]
        public Animation DeformPhase { get; } = new(0, -360000, 360000);

        // ばらつき
        [Display(GroupName = Scatter, Name = "位置X", Description = "横方向へ点をばらつかせる量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation ScatterX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "位置Y", Description = "縦方向へ点をばらつかせる量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation ScatterY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "位置Z", Description = "奥行き方向へ点をばらつかせる量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation ScatterZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "シード", Description = "ばらつき方を変えます")]
        [AnimationSlider("F0", "", 0, 100)]
        public Animation Seed { get; } = new(0, 0, 10000);

        // 配置
        [Display(GroupName = Place, Name = "位置X")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PositionX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "位置Y")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PositionY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "位置Z")]
        [AnimationSlider("F1", "px", -500, 500)]
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

        public PixelPoints3DEffect()
        {
            SubscribeChildUndoRedoable(PointSettings);
            SubscribeChildUndoRedoable(LineSettings);
            SubscribeChildUndoRedoable(FaceSettings);
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            if (DrawPoints && PointSettings.IsEmpty)
                PointSettings = [new PointParams()];
            else if (!DrawPoints && !PointSettings.IsEmpty)
                PointSettings = [];

            if (DrawLines && LineSettings.IsEmpty)
                LineSettings = [new LineParams()];
            else if (!DrawLines && !LineSettings.IsEmpty)
                LineSettings = [];

            if (DrawFaces && FaceSettings.IsEmpty)
                FaceSettings = [new FaceParams()];
            else if (!DrawFaces && !FaceSettings.IsEmpty)
                FaceSettings = [];
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new PixelPoints3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
        [
            SpacingX, SpacingY, SpacingZ, Depth, Threshold,
            ..PointSettings, ..LineSettings, ..FaceSettings,
            DeformAmount, DeformPeriod, DeformPhase,
            ScatterX, ScatterY, ScatterZ, Seed,
            PositionX, PositionY, PositionZ, Scale,
            RotationX, RotationY, RotationZ,
            CameraSyncAnimation,
        ];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    /// <summary>点のパラメータ。</summary>
    public class PointParams : Animatable
    {
        [Display(GroupName = "点", Name = "形", Description = "粒の形")]
        [EnumComboBox]
        public PointShape Shape { get => shape; set => Set(ref shape, value); }
        private PointShape shape = PointShape.Square;

        [Display(GroupName = "点", Name = "大きさ", Description = "四角形なら一辺、円なら直径の長さ")]
        [AnimationSlider("F1", "px", 0, 50)]
        public Animation Size { get; } = new(4, 0, 500);

        [Display(GroupName = "点", Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        public PointColorSource ColorSource { get => colorSource; set => Set(ref colorSource, value); }
        private PointColorSource colorSource = PointColorSource.Solid;

        [Display(GroupName = "点", Name = "色", Description = "粒の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(ColorSource), PointColorSource.Solid)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Size];
    }

    /// <summary>線のパラメータ。</summary>
    public class LineParams : Animatable
    {
        [Display(GroupName = "線", Name = "太さ", Description = "つなぐ線の太さ")]
        [AnimationSlider("F1", "px", 0, 20)]
        public Animation Width { get; } = new(2, 0, 500);

        [Display(GroupName = "線", Name = "ランダム", Description = "線を引かない割合。上げるほど、つながり方がまばらになります")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Randomness { get; } = new(0, 0, 100);

        [Display(GroupName = "線", Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        public PointColorSource ColorSource { get => colorSource; set => Set(ref colorSource, value); }
        private PointColorSource colorSource = PointColorSource.Solid;

        [Display(GroupName = "線", Name = "色", Description = "線の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(ColorSource), PointColorSource.Solid)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Width, Randomness];
    }

    /// <summary>面のパラメータ。</summary>
    public class FaceParams : Animatable
    {
        [Display(GroupName = "面", Name = "不透明度", Description = "面だけに掛かる不透明度")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Opacity { get; } = new(100, 0, 100);

        [Display(GroupName = "面", Name = "不透明度のばらつき", Description = "面ごとに不透明度をランダムに散らす量")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation OpacityRandomness { get; } = new(0, 0, 100);

        [Display(GroupName = "面", Name = "色の種類", Description = "単色で塗るか、その位置の画像の色を使うか")]
        [EnumComboBox]
        public PointColorSource ColorSource { get => colorSource; set => Set(ref colorSource, value); }
        private PointColorSource colorSource = PointColorSource.Image;

        [Display(GroupName = "面", Name = "色", Description = "面の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(ColorSource), PointColorSource.Solid)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Opacity, OpacityRandomness];
    }

    /// <summary>点の並びをどう歪ませるか。</summary>
    /// <remarks>
    /// 並び方そのものは平らな格子のままで、位置だけをあとから曲げます。
    /// 元の絵の形は保たれたまま歪むので、文字や立ち絵の輪郭が分かる状態で
    /// 立体的に動かせます。
    /// <para>
    /// 値はシェーダーへそのまま渡ります。並び順を変えると見た目が変わります。
    /// </para>
    /// </remarks>
    public enum DeformKind
    {
        [Display(Name = "なし", Description = "歪ませません")]
        None,

        [Display(Name = "波", Description = "選んだ軸に沿って進む波で、奥行き方向に押し引きします")]
        Wave,

        [Display(Name = "ねじれ", Description = "選んだ軸のまわりに、進むほど強くひねります")]
        Twist,

        [Display(Name = "膨らみ", Description = "軸の中心ほど大きく持ち上げて、丸く盛り上げます")]
        Bulge,

        [Display(Name = "球に巻く", Description = "平らな並びを、選んだ軸を極とする球の面に貼り付けます")]
        Sphere,
    }

    /// <summary>変形の軸。</summary>
    public enum DeformAxis
    {
        [Display(Name = "X軸", Description = "横")]
        X,

        [Display(Name = "Y軸", Description = "縦")]
        Y,

        [Display(Name = "Z軸", Description = "奥行き")]
        Z,
    }

    /// <summary>粒の形。</summary>
    /// <remarks>
    /// どちらも同じ四角形の板を描き、円は縁を丸く抜きます。板の数も頂点の数も
    /// 変わらないので、切り替えても形状の作り直しは起きません。
    /// </remarks>
    public enum PointShape
    {
        [Display(Name = "四角形", Description = "四角い粒を描きます")]
        Square,

        [Display(Name = "円", Description = "丸い粒を描きます")]
        Circle,
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
