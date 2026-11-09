using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Tachie
{
    public class Model3DCharacterParameter : TachieCharacterParameterBase
    {
        private const string Body = "";
        private const string Rotation = "向き";
        private const string Paint = "色";
        private const string Toon = "トーン";
        private const string Feel = "質感";
        private const string Talk = "口パク";
        private const string Blink = "まばたき";

        [Display(GroupName = Body, Name = "ファイル", Description = "obj / gltf / glb / vrm / fbx のモデル")]
        [FileSelector(FileGroupType.None, CustomFilterName = "3Dモデル", CustomFilterValue = "*.obj;*.gltf;*.glb;*.vrm;*.fbx")]
        public string File { get => file; set => Set(ref file, value ?? string.Empty); }
        private string file = string.Empty;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡しが何 px になるか")]
        [TextBoxSlider("F0", "px", 100, 2000)]
        public double Size { get => size; set => Set(ref size, Math.Clamp(value, 1, 100000)); }
        private double size = 1000;

        [Display(GroupName = Rotation, Name = "X")]
        [TextBoxSlider("F1", "°", -180, 180)]
        public double RotationX { get => rotationX; set => Set(ref rotationX, value); }
        private double rotationX;

        [Display(GroupName = Rotation, Name = "Y")]
        [TextBoxSlider("F1", "°", -180, 180)]
        public double RotationY { get => rotationY; set => Set(ref rotationY, value); }
        private double rotationY;

        [Display(GroupName = Rotation, Name = "Z")]
        [TextBoxSlider("F1", "°", -180, 180)]
        public double RotationZ { get => rotationZ; set => Set(ref rotationZ, value); }
        private double rotationZ;

        [Display(GroupName = Body, Name = "揺らす",
            Description = "揺れもの（髪やスカートなど）と、ボーンの「揺らす」を効かせます")]
        [ToggleSlider]
        public bool Swings { get => swings; set => Set(ref swings, value); }
        private bool swings = true;

        [Display(GroupName = Body, Name = "上下の位置", Description = "立ち絵の中でモデルを上下にずらします")]
        [TextBoxSlider("F0", "px", -1000, 1000)]
        public double OffsetY { get => offsetY; set => Set(ref offsetY, value); }
        private double offsetY;

        [Display(GroupName = Paint, Name = "色", Description = "モデルの色に掛け合わせます。白ならそのまま")]
        [ColorPicker]
        public Color Tint { get => tint; set => Set(ref tint, value); }
        private Color tint = Colors.White;

        [Display(GroupName = Paint, Name = "陰影", Description = "光源にしたがって陰影をつけます。切ると、モデルの色のまま塗ります")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsUnlit
        {
            get => isUnlit;
            set
            {
                Set(ref isUnlit, value);
                OnPropertyChanged(nameof(IsLit));
                OnPropertyChanged(nameof(IsShaded));
            }
        }
        private bool isUnlit;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsToon
        {
            get => isToon;
            set
            {
                Set(ref isToon, value);
                OnPropertyChanged(nameof(IsShaded));
                OnPropertyChanged(nameof(ToonSteps));
            }
        }
        private bool isToon = true;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int ToonLevels
        {
            get => toonLevels;
            set
            {
                Set(ref toonLevels, Math.Clamp(value, 2, 8));
                OnPropertyChanged(nameof(ToonSteps));
            }
        }
        private int toonLevels = 2;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsShaded => !IsUnlit && ToonSteps >= 2;

        [Display(GroupName = Toon, Name = "段数",
            Description = "明るさを何段に分けて塗るか。1 なら段に分けず、なめらかに塗ります")]
        [TextBoxSlider("F0", "段", 1, 5)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public int ToonSteps
        {
            get => IsToon ? ToonLevels : 1;
            set
            {
                var steps = Math.Clamp(value, 1, 8);

                if (steps >= 2)
                    ToonLevels = steps;

                IsToon = steps >= 2;
            }
        }

        [Display(GroupName = Toon, Name = "影の境目",
            Description = "影のできる境目をずらします。上げると影が小さく、下げると大きくなります")]
        [TextBoxSlider("F0", "%", -50, 50)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double ToonShift
        {
            get => toonShift;
            set => Set(ref toonShift, Math.Clamp(value, -100, 100));
        }
        private double toonShift;

        [Display(GroupName = Toon, Name = "境目のぼかし",
            Description = "段の境目をどれだけぼかすか")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShaded), true)]
        public double ToonFeather
        {
            get => toonFeather;
            set => Set(ref toonFeather, Math.Clamp(value, 0, 100));
        }
        private double toonFeather = ModelPaint.DefaultFeather * FeatherSpan;

        [Display(GroupName = Toon, Name = "影の色",
            Description = "光の当たらないところの色に掛け合わせます。白ならモデルに書かれたまま")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Color ShadeColor { get => shadeColor; set => Set(ref shadeColor, value); }
        private Color shadeColor = Colors.White;

        [Display(GroupName = Feel, Name = "凹凸",
            Description = "モデルに入っている法線マップのでこぼこの強さ")]
        [TextBoxSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Bump { get => bump; set => Set(ref bump, Math.Clamp(value, 0, 1000)); }
        private double bump = 100;

        [Display(GroupName = Feel, Name = "発光",
            Description = "モデルに書かれている光り方の強さ。陰影を切っていても光ります")]
        [TextBoxSlider("F0", "%", 0, 300)]
        public double Glow { get => glow; set => Set(ref glow, Math.Clamp(value, 0, 10000)); }
        private double glow = 100;

        [Display(GroupName = Feel, Name = "金属",
            Description = "上げるほど金属らしくなり、映り込みが地の色に染まります")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Metallic { get => metallic; set => Set(ref metallic, Math.Clamp(value, 0, 100)); }
        private double metallic;

        [Display(GroupName = Feel, Name = "つるつる",
            Description = "上げるほど表面がなめらかになり、映り込みがはっきりします")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Smoothness { get => smoothness; set => Set(ref smoothness, Math.Clamp(value, 0, 100)); }
        private double smoothness;

        [Display(GroupName = Feel, Name = "映り込み",
            Description = "「3D環境」のアイテムで選んだ風景の画像を映します")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Mirror { get => mirror; set => Set(ref mirror, Math.Clamp(value, 0, 100)); }
        private double mirror;

        internal const float FeatherSpan = 400f;

        [Display(GroupName = Talk, Name = "口の表情",
            Description = "セリフに合わせて動かす「あ」の口。い・う・え・お は自動で使います")]
        [ExpressionSelector]
        public string MouthExpression { get => mouth; set => Set(ref mouth, value ?? string.Empty); }
        private string mouth = "A";

        [Display(GroupName = Talk, Name = "口の開き", Description = "いちばん開いたときの強さ")]
        [TextBoxSlider("F0", "%", 0, 100)]
        public double MouthOpen { get => mouthOpen; set => Set(ref mouthOpen, Math.Clamp(value, 0, 100)); }
        private double mouthOpen = 100;

        [Display(GroupName = Talk, Name = "なめらかさ",
            Description = "次の口の形へ移るのにかける時間")]
        [TextBoxSlider("F0", "ms", 0, 150)]
        public double MouthSmooth
        {
            get => mouthSmooth;
            set => Set(ref mouthSmooth, Math.Clamp(value, 0, 1000));
        }
        private double mouthSmooth = 45;

        [Display(GroupName = Blink, Name = "まばたきの表情",
            Description = "まばたきに使う表情")]
        [ExpressionSelector]
        public string BlinkExpression { get => blink; set => Set(ref blink, value ?? string.Empty); }
        private string blink = "Blink";

        [Display(GroupName = Blink, Name = "間隔", Description = "何秒ごとにまばたきするか")]
        [TextBoxSlider("F1", "秒", 1, 15)]
        public double BlinkInterval { get => blinkInterval; set => Set(ref blinkInterval, Math.Clamp(value, 0.2, 60)); }
        private double blinkInterval = 4;

        [Display(GroupName = Blink, Name = "閉じている時間")]
        [TextBoxSlider("F2", "秒", 0.05, 0.5)]
        public double BlinkLength { get => blinkLength; set => Set(ref blinkLength, Math.Clamp(value, 0.02, 2)); }
        private double blinkLength = 0.15;
    }
}
