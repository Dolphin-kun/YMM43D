using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace PixelPoints3D
{
    [VideoEffect("点群3D", ["3D"], ["点群", "点", "粒", "ドット", "パーティクル", "point cloud", "particle"])]
    public class PixelPoints3DEffect : VideoEffect3DBase, ISceneRotation3D
    {
        private const string Grid = "点群3D";
        private const string Shape = "形";
        private const string Deform = "変形";
        private const string Scatter = "ばらつき";
        private const string Place = "配置";

        public override string Label => "点群3D";

        [Display(GroupName = Grid, Name = "距離X", Description = "X軸方向の点と点の間隔")]
        [AnimationSlider("F1", "px", 1, 200)]
        public Animation SpacingX { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "距離Y", Description = "Y軸方向の点と点の間隔")]
        [AnimationSlider("F1", "px", 1, 200)]
        public Animation SpacingY { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "距離Z", Description = "Z軸方向の点と点の間隔")]
        [AnimationSlider("F1", "px", 1, 200)]
        public Animation SpacingZ { get; } = new(20, 1, 2000);

        [Display(GroupName = Grid, Name = "奥行き", Description = "点を重ねる奥行きの厚み")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Depth { get; } = new(0, 0, 5000);

        [Display(GroupName = Grid, Name = "しきい値", Description = "この不透明度より薄いところには点を打ちません")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Threshold { get; } = new(50, 0, 100);

        [Display(GroupName = Shape, Name = "点を描く", Description = "格子の各点に粒を描きます")]
        [ToggleSlider]
        public bool DrawPoints { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Shape, Name = "線を描く", Description = "隣り合う点どうしを線でつなぎます")]
        [ToggleSlider]
        public bool DrawLines { get; set => Set(ref field, value); }

        [Display(GroupName = Shape, Name = "面を描く", Description = "隣り合う4点を三角形2枚で埋めます")]
        [ToggleSlider]
        public bool DrawFaces { get; set => Set(ref field, value); }

        [Display(GroupName = Shape, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit)); }

        [Display(GroupName = Shape, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Shape, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        [Display(AutoGenerateField = true)]
        public ImmutableList<PointParams> PointSettings { get; set => Set(ref field, value); } = [new PointParams()];

        [Display(AutoGenerateField = true)]
        public ImmutableList<LineParams> LineSettings { get; set => Set(ref field, value); } = [];

        [Display(AutoGenerateField = true)]
        public ImmutableList<FaceParams> FaceSettings { get; set => Set(ref field, value); } = [];

        [Display(GroupName = Deform, Name = "種類", Description = "点の並びをどう歪ませるか")]
        [EnumComboBox]
        public DeformKind DeformKind { get; set => Set(ref field, value, nameof(DeformKind), nameof(IsDeformed), nameof(IsWave)); } = DeformKind.None;

        public bool IsDeformed => DeformKind != DeformKind.None;

        public bool IsWave => DeformKind == DeformKind.Wave;

        [Display(GroupName = Deform, Name = "強さ", Description = "歪ませる量で、負にすると逆向きに歪みます")]
        [AnimationSlider("F0", "%", -100, 100)]
        [ShowPropertyEditorWhen(nameof(IsDeformed), true)]
        public Animation DeformAmount { get; } = new(50, -100000, 100000);

        [Display(GroupName = Deform, Name = "向き", Description = "変形の軸で、波が進む向き・ねじれの回転軸・球の極になります")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsDeformed), true)]
        public DeformAxis DeformAxis { get; set => Set(ref field, value); } = DeformAxis.Y;

        [Display(GroupName = Deform, Name = "周期", Description = "波1つ分の長さ")]
        [AnimationSlider("F1", "px", 10, 1000)]
        [ShowPropertyEditorWhen(nameof(IsWave), true)]
        public Animation DeformPeriod { get; } = new(200, 1, 100000);

        [Display(GroupName = Deform, Name = "位相", Description = "波やねじれのずれ")]
        [AnimationSlider("F0", "°", -360, 360)]
        [ShowPropertyEditorWhen(nameof(IsDeformed), true)]
        public Animation DeformPhase { get; } = new(0, -360000, 360000);

        [Display(GroupName = Scatter, Name = "位置X", Description = "横方向へ点をばらつかせる量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation ScatterX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "位置Y", Description = "縦方向へ点をばらつかせる量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation ScatterY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "位置Z", Description = "奥行き方向へ点をばらつかせる量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation ScatterZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Scatter, Name = "シード", Description = "ばらつき方を決める乱数の種")]
        [AnimationSlider("F0", "", 0, 100)]
        public Animation Seed { get; } = new(0, 0, 10000);

        [Display(GroupName = Place, Name = "位置X", Description = "点群全体を横に動かす量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PositionX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "位置Y", Description = "点群全体を縦に動かす量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PositionY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "位置Z", Description = "点群全体を奥行き方向に動かす量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PositionZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "大きさ", Description = "点群全体の大きさ")]
        [AnimationSlider("F1", "%", 0, 400)]
        public Animation Scale { get; } = new(100, 0, 10000);

        [Display(GroupName = Place, Name = "回転X", Description = "点群全体の X 軸まわりの回転")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationX { get; } = new(0, -36000, 36000);

        [Display(GroupName = Place, Name = "回転Y", Description = "点群全体の Y 軸まわりの回転")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationY { get; } = new(0, -36000, 36000);

        [Display(GroupName = Place, Name = "回転Z", Description = "点群全体の Z 軸まわりの回転")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationZ { get; } = new(0, -36000, 36000);

        internal PointParams? Point => DrawPoints ? PointSettings.FirstOrDefault() : null;

        internal LineParams? Line => DrawLines ? LineSettings.FirstOrDefault() : null;

        internal FaceParams? Face => DrawFaces ? FaceSettings.FirstOrDefault() : null;

        internal SurfaceGloss GetGloss(in FrameContext time) => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            PointSettings = Sync(DrawPoints, PointSettings);
            LineSettings = Sync(DrawLines, LineSettings);
            FaceSettings = Sync(DrawFaces, FaceSettings);
        }

        private static ImmutableList<T> Sync<T>(bool enabled, ImmutableList<T> current) where T : new() => enabled != current.IsEmpty ? current : enabled ? [new T()] : [];

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new PixelPoints3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [SpacingX, SpacingY, SpacingZ, Depth, Threshold, .. PointSettings, .. LineSettings, .. FaceSettings, DeformAmount, DeformPeriod, DeformPhase, ScatterX, ScatterY, ScatterZ, Seed, PositionX, PositionY, PositionZ, Scale, RotationX, RotationY, RotationZ, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
