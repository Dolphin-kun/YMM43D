using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Tachie
{
    public class Model3DCharacterParameter : TachieCharacterParameterBase
    {
        private const string Body = "3Dモデル";
        private const string Rotation = "向き";
        private const string Paint = "色";
        private const string Toon = "トーン";
        private const string Feel = "質感";
        private const string Talk = "口パク";
        private const string Blink = "まばたき";

        internal const float FeatherSpan = 400f;

        [Display(GroupName = Body, Name = "ファイル", Description = "obj / gltf / glb / vrm / fbx / pmx のモデル")]
        [FileSelector(FileGroupType.None, CustomFilterName = "3Dモデル", CustomFilterValue = "*.obj;*.gltf;*.glb;*.vrm;*.fbx;*.pmx")]
        public string File { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡しが何 px になるか")]
        [TextBoxSlider("F0", "px", 100, 2000)]
        public double Size { get; set => Set(ref field, Math.Clamp(value, 1, 100000)); } = 1000;

        [Display(GroupName = Rotation, Name = "X")]
        [TextBoxSlider("F1", "°", -180, 180)]
        public double RotationX { get; set => Set(ref field, value); }

        [Display(GroupName = Rotation, Name = "Y")]
        [TextBoxSlider("F1", "°", -180, 180)]
        public double RotationY { get; set => Set(ref field, value); }

        [Display(GroupName = Rotation, Name = "Z")]
        [TextBoxSlider("F1", "°", -180, 180)]
        public double RotationZ { get; set => Set(ref field, value); }

        [Display(GroupName = Body, Name = "揺らす", Description = "揺れもの（髪やスカートなど）と、ボーンの「揺らす」を効かせます")]
        [ToggleSlider]
        public bool Swings { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Body, Name = "やわらかさ", Description = "揺れものの揺れやすさ")]
        [TextBoxSlider("F0", "%", 0, 300)]
        [ShowPropertyEditorWhen(nameof(Swings), true)]
        public double Softness { get; set => Set(ref field, Math.Clamp(value, 0, 100000)); } = 100;

        [Display(GroupName = Body, Name = "揺れの長さ", Description = "揺れが続く長さ")]
        [TextBoxSlider("F0", "%", 0, 300)]
        [ShowPropertyEditorWhen(nameof(Swings), true)]
        public double Sway { get; set => Set(ref field, Math.Clamp(value, 0, 100000)); } = 200;

        [Display(GroupName = Body, Name = "上下の位置", Description = "立ち絵の中でモデルを上下にずらします")]
        [TextBoxSlider("F0", "px", -1000, 1000)]
        public double OffsetY { get; set => Set(ref field, value); }

        [Display(GroupName = Paint, Name = "色", Description = "モデルの色に掛け合わせる色")]
        [ColorPicker]
        public Color Tint { get => tint; set => Set(ref tint, value); }
        private Color tint = Colors.White;

        [Display(GroupName = Paint, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit), nameof(IsShaded)); }

        public bool IsToon { get; set => Set(ref field, value, nameof(IsToon), nameof(IsShaded), nameof(ToonSteps)); } = true;

        public int ToonLevels { get; set => Set(ref field, Math.Clamp(value, 2, 8), nameof(ToonLevels), nameof(ToonSteps)); } = 2;

        public bool IsShaded => !IsUnlit && ToonSteps >= 2;

        [Display(GroupName = Toon, Name = "段数", Description = "明るさを何段に分けて塗るかで、1 なら段に分けずなめらかに塗ります")]
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

        [Display(GroupName = Toon, Name = "影の境目", Description = "影のできる境目をずらす量")]
        [TextBoxSlider("F0", "%", -50, 50)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double ToonShift { get; set => Set(ref field, Math.Clamp(value, -100, 100)); }

        [Display(GroupName = Toon, Name = "境目のぼかし", Description = "段の境目をどれだけぼかすか")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShaded), true)]
        public double ToonFeather { get; set => Set(ref field, Math.Clamp(value, 0, 100)); } = ModelPaint.DefaultFeather * FeatherSpan;

        [Display(GroupName = Toon, Name = "影の色", Description = "光の当たらないところの色に掛け合わせる色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Color ShadeColor { get => shadeColor; set => Set(ref shadeColor, value); }
        private Color shadeColor = Colors.White;

        [Display(GroupName = Feel, Name = "凹凸", Description = "モデルに入っている法線マップのでこぼこの強さ")]
        [TextBoxSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Bump { get; set => Set(ref field, Math.Clamp(value, 0, 1000)); } = 100;

        [Display(GroupName = Feel, Name = "発光", Description = "モデルに書かれている光り方の強さで、陰影を切っていても光ります")]
        [TextBoxSlider("F0", "%", 0, 300)]
        public double Glow { get; set => Set(ref field, Math.Clamp(value, 0, 10000)); } = 100;

        [Display(GroupName = Feel, Name = "金属", Description = "金属らしさ")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Metallic { get; set => Set(ref field, Math.Clamp(value, 0, 100)); }

        [Display(GroupName = Feel, Name = "つるつる", Description = "表面のなめらかさ")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Smoothness { get; set => Set(ref field, Math.Clamp(value, 0, 100)); }

        [Display(GroupName = Feel, Name = "映り込み", Description = "「3D環境」のアイテムで選んだ風景の画像を映します")]
        [TextBoxSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public double Mirror { get; set => Set(ref field, Math.Clamp(value, 0, 100)); }

        [Display(GroupName = Talk, Name = "口の表情", Description = "セリフに合わせて動かす「あ」の口で、い・う・え・お は自動で使います")]
        [ExpressionSelector]
        public string MouthExpression { get; set => Set(ref field, value ?? string.Empty); } = "A";

        [Display(GroupName = Talk, Name = "口の開き", Description = "いちばん開いたときの強さ")]
        [TextBoxSlider("F0", "%", 0, 100)]
        public double MouthOpen { get; set => Set(ref field, Math.Clamp(value, 0, 100)); } = 100;

        [Display(GroupName = Talk, Name = "なめらかさ", Description = "次の口の形へ移るのにかける時間")]
        [TextBoxSlider("F0", "ms", 0, 150)]
        public double MouthSmooth { get; set => Set(ref field, Math.Clamp(value, 0, 1000)); } = 45;

        [Display(GroupName = Blink, Name = "まばたきの表情", Description = "まばたきに使う表情")]
        [ExpressionSelector]
        public string BlinkExpression { get; set => Set(ref field, value ?? string.Empty); } = "Blink";

        [Display(GroupName = Blink, Name = "間隔", Description = "何秒ごとにまばたきするか")]
        [TextBoxSlider("F1", "秒", 1, 15)]
        public double BlinkInterval { get; set => Set(ref field, Math.Clamp(value, 0.2, 60)); } = 4;

        [Display(GroupName = Blink, Name = "閉じている時間", Description = "まばたきで目を閉じている長さ")]
        [TextBoxSlider("F2", "秒", 0.05, 0.5)]
        public double BlinkLength { get; set => Set(ref field, Math.Clamp(value, 0.02, 2)); } = 0.15;
    }
}
