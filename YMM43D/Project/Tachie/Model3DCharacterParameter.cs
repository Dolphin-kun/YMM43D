using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Tachie
{
    // 3Dモデルの立ち絵。キャラクターごとの設定。
    public class Model3DCharacterParameter : TachieCharacterParameterBase
    {
        private const string Body = "";
        private const string Rotation = "向き";
        private const string Paint = "色";
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

        [Display(GroupName = Body, Name = "上下の位置", Description = "立ち絵の中でモデルを上下にずらします")]
        [TextBoxSlider("F0", "px", -1000, 1000)]
        public double OffsetY { get => offsetY; set => Set(ref offsetY, value); }
        private double offsetY;

        [Display(GroupName = Paint, Name = "色", Description = "モデルの色に掛け合わせます。白ならそのまま")]
        [ColorPicker]
        public Color Tint { get => tint; set => Set(ref tint, value); }
        private Color tint = Colors.White;

        [Display(GroupName = Paint, Name = "陰影をつけない", Description = "光源を無視して、モデルの色のまま塗ります")]
        [ToggleSlider]
        public bool IsUnlit
        {
            get => isUnlit;
            set
            {
                Set(ref isUnlit, value);
                OnPropertyChanged(nameof(IsShaded));
            }
        }
        private bool isUnlit;

        [Display(GroupName = Paint, Name = "アニメ塗り", Description = "陰影を決まった段数に分けて塗ります")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public bool IsToon
        {
            get => isToon;
            set
            {
                Set(ref isToon, value);
                OnPropertyChanged(nameof(IsShaded));
            }
        }
        private bool isToon = true;

        [Display(GroupName = Paint, Name = "塗りの段数")]
        [TextBoxSlider("F0", "段", 2, 5)]
        [ShowPropertyEditorWhen(nameof(IsShaded), true)]
        public int ToonLevels { get => toonLevels; set => Set(ref toonLevels, Math.Clamp(value, 2, 8)); }
        private int toonLevels = 2;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public bool IsShaded => !IsUnlit && IsToon;

        [Display(GroupName = Talk, Name = "口の表情",
            Description = "セリフに合わせて動かす表情の名前。VRM なら A（あ）が入っています")]
        [TextEditor(AcceptsReturn = false)]
        public string MouthExpression { get => mouth; set => Set(ref mouth, value ?? string.Empty); }
        private string mouth = "A";

        [Display(GroupName = Talk, Name = "口の開き", Description = "いちばん開いたときの強さ")]
        [TextBoxSlider("F0", "%", 0, 100)]
        public double MouthOpen { get => mouthOpen; set => Set(ref mouthOpen, Math.Clamp(value, 0, 100)); }
        private double mouthOpen = 100;

        [Display(GroupName = Blink, Name = "まばたきの表情",
            Description = "まばたきに使う表情の名前。空にするとまばたきしません")]
        [TextEditor(AcceptsReturn = false)]
        public string BlinkExpression { get => blink; set => Set(ref blink, value ?? string.Empty); }
        private string blink = "Blink";

        [Display(GroupName = Blink, Name = "間隔", Description = "何秒ごとにまばたきするか")]
        [TextBoxSlider("F1", "秒", 1, 15)]
        public double BlinkInterval { get => blinkInterval; set => Set(ref blinkInterval, Math.Clamp(value, 0.2, 60)); }
        private double blinkInterval = 4;

        [Display(GroupName = Blink, Name = "閉じている時間")]
        [TextBoxSlider("F2", "秒", 0.05, 0.5)]
        public double BlinkLength { get => blinkLength; set => Set(ref blinkLength, Math.Clamp(value, 0.02, 2)); }
        private double blinkLength = 0.12;
    }
}
