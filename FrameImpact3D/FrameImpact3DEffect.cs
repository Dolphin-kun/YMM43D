using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace FrameImpact3D
{
    [VideoEffect("フレームインパクト3D", ["3D"], ["フレームインパクト", "インパクトフレーム", "インパクト", "衝撃", "衝撃波", "打撃", "ヒット", "爆発", "強調", "閃光", "集中線", "放射線", "火花", "揺れ", "白黒反転", "ネオン", "色ずれ", "impact frame", "impact", "hit", "shockwave", "burst", "flash", "spark"])]
    public class FrameImpact3DEffect : VideoEffect3DBase, IPlacedSceneLightSource
    {
        private const string Impact = "フレームインパクト3D";
        private const string Placement = "配置";
        private const string Motion = "アニメーション";
        private const string Frames = "インパクトフレーム";
        private const string Shock = "衝撃波";
        private const string Lines = "放射線";
        private const string Flash = "閃光";
        private const string Sparks = "火花";

        private const float FlashSpan = 0.4f;
        private const int RoundSeedStep = 7919;

        public const double ShortestDuration = 0.05;
        public const int LongestKoma = 30;

        public override string Label => "フレームインパクト3D";

        [Display(GroupName = Impact, Name = "ぼかし", Description = "衝撃の直前に絵をぼかし、色をずらしてにじませます。向きを決めたときはその向きに流れます")]
        [ToggleSlider]
        public bool IsBlurEnabled { get; set => Set(ref field, value, nameof(IsBlurEnabled), nameof(Repaints), nameof(ShowsArea)); } = true;

        [Display(GroupName = Impact, Name = "インパクトフレーム", Description = "衝撃の瞬間、数コマだけ絵を塗り替えます")]
        [ToggleSlider]
        public bool IsFramesEnabled { get; set => Set(ref field, value, nameof(IsFramesEnabled), nameof(Repaints), nameof(ShowsArea), nameof(ShowsBurstColor), nameof(ShowsRimColor), nameof(ShowsNeonLook)); } = true;

        [Display(GroupName = Impact, Name = "衝撃波", Description = "中心から広がる輪を出し、向きを決めたときは広がりの幅だけの弧になります")]
        [ToggleSlider]
        public bool IsShockEnabled { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Impact, Name = "放射線", Description = "中心から外へ飛び出す鋭い線を出します")]
        [ToggleSlider]
        public bool IsLinesEnabled { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Impact, Name = "閃光", Description = "中心を一瞬強く光らせ、向きを決めたときはその向きへ伸びます")]
        [ToggleSlider]
        public bool IsFlashEnabled { get; set => Set(ref field, value, nameof(IsFlashEnabled), nameof(IsLighting)); } = true;

        [Display(GroupName = Impact, Name = "揺れ", Description = "アイテムを一瞬細かく揺らし、向きを決めたときはその向きに大きく揺らします")]
        [ToggleSlider]
        public bool IsShakeEnabled { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Impact, Name = "火花", Description = "中心から火花の粒を飛び散らせます")]
        [ToggleSlider]
        public bool IsSparksEnabled { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Impact, Name = "乱数", Description = "線と火花の散らばり方を変える番号")]
        [TextBoxSlider("F0", "", 0, 100)]
        [Range(0, 9999)]
        public int Seed { get; set => Set(ref field, Math.Clamp(value, 0, 9999)); }

        [Display(GroupName = Placement, Name = "中心X", Description = "衝撃の中心を横にずらす量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation CenterX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Placement, Name = "中心Y", Description = "衝撃の中心を縦にずらす量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation CenterY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Placement, Name = "中心Z", Description = "衝撃の中心を奥行き方向にずらす量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation CenterZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Placement, Name = "面", Description = "衝撃波が広がり、角度を測る面")]
        [EnumComboBox]
        public ImpactPlane Plane { get; set => Set(ref field, value); } = ImpactPlane.Screen;

        [Display(GroupName = Placement, Name = "出方", Description = "中心からまわり全体へ広げるか、決めた向きへ飛ばすか")]
        [EnumComboBox]
        public ImpactEmission Emission { get; set => Set(ref field, value, nameof(Emission), nameof(IsAimed)); } = ImpactEmission.All;

        public bool IsAimed => Emission == ImpactEmission.Aimed;

        [Display(GroupName = Placement, Name = "角度", Description = "衝撃が飛ぶ向きで、0° が右、90° が下")]
        [AnimationSlider("F0", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(IsAimed), true)]
        public Animation Angle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Placement, Name = "広がり", Description = "角度を中心に、衝撃が飛ぶ範囲の幅")]
        [AnimationSlider("F0", "°", 0, 360)]
        [ShowPropertyEditorWhen(nameof(IsAimed), true)]
        public Animation Spread { get; } = new(60, 0, 360);

        [Display(GroupName = Motion, Name = "開始位置", Description = "アイテムの頭から衝撃が起きるまでの時間")]
        [TextBoxSlider("F2", "秒", 0, 10)]
        [Range(0, 3600)]
        public double Start { get; set => Set(ref field, Math.Clamp(value, 0, 3600)); }

        [Display(GroupName = Motion, Name = "長さ", Description = "衝撃が起きてから衝撃波や火花が消えるまでの時間")]
        [TextBoxSlider("F2", "秒", ShortestDuration, 3)]
        [Range(ShortestDuration, 60)]
        public double Duration { get; set => Set(ref field, Math.Clamp(value, ShortestDuration, 60)); } = 0.6;

        [Display(GroupName = Motion, Name = "間隔", Description = "この間隔で衝撃をくり返し、0 なら 1 回だけ起こします")]
        [TextBoxSlider("F2", "秒", 0, 5)]
        [Range(0, 3600)]
        public double Interval { get; set => Set(ref field, Math.Clamp(value, 0, 3600)); }

        [Display(GroupName = Motion, Name = "ためる時間", Description = "衝撃の何秒前からぼけ始めるか")]
        [TextBoxSlider("F2", "秒", 0.05, 1)]
        [Range(0.01, 60)]
        [ShowPropertyEditorWhen(nameof(IsBlurEnabled), true)]
        public double BuildUp { get; set => Set(ref field, Math.Clamp(value, 0.01, 60)); } = 0.25;

        [Display(GroupName = Motion, Name = "1コマの長さ", Description = "インパクトフレームで塗り替えた絵 1 枚を出しておく長さ")]
        [TextBoxSlider("F0", "F", 1, 10)]
        [Range(1, LongestKoma)]
        [ShowPropertyEditorWhen(nameof(IsFramesEnabled), true)]
        public int KomaLength { get; set => Set(ref field, Math.Clamp(value, 1, LongestKoma)); } = 2;

        [Display(GroupName = Motion, Name = "揺れ幅", Description = "揺れ始めのずれの大きさ")]
        [AnimationSlider("F0", "px", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShakeEnabled), true)]
        public Animation ShakeAmount { get; } = new(20, 0, 10000);

        [Display(GroupName = Motion, Name = "揺れの速さ", Description = "1 秒に揺れる回数")]
        [AnimationSlider("F0", "回", 1, 60)]
        [ShowPropertyEditorWhen(nameof(IsShakeEnabled), true)]
        public Animation ShakeSpeed { get; } = new(30, 1, 1000);

        public bool Repaints => IsBlurEnabled || IsFramesEnabled;

        [Display(GroupName = Frames, Name = "塗り替え範囲", Description = "ぼかしとインパクトフレームで絵を塗り替える範囲")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(Repaints), true)]
        public ImpactArea Coverage { get; set => Set(ref field, value, nameof(Coverage), nameof(IsAroundCenter), nameof(ShowsArea)); } = ImpactArea.Whole;

        public bool IsAroundCenter => Coverage == ImpactArea.AroundCenter;

        public bool ShowsArea => Repaints && IsAroundCenter;

        [Display(GroupName = Frames, Name = "半径", Description = "中心から塗り替える範囲の半径")]
        [AnimationSlider("F0", "px", 0, 2000)]
        [ShowPropertyEditorWhen(nameof(ShowsArea), true)]
        public Animation AreaRadius { get; } = new(600, 0, 100000);

        [Display(GroupName = Frames, Name = "境目のぼかし", Description = "塗り替える範囲の境目をなじませる幅")]
        [AnimationSlider("F0", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(ShowsArea), true)]
        public Animation AreaSoftness { get; } = new(250, 0, 100000);

        [Display(GroupName = Frames, Name = "拡散", Description = "インパクトフレームの間に、範囲を中心から半径まで広げます")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(ShowsArea), true)]
        public bool AreaExpands { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Frames, Name = "ぼかし", Description = "衝撃の直前のぼかしの強さ")]
        [AnimationSlider("F0", "px", 0, 50)]
        [ShowPropertyEditorWhen(nameof(IsBlurEnabled), true)]
        public Animation BlurAmount { get; } = new(10, 0, 1000);

        [Display(GroupName = Frames, Name = "色ずれ", Description = "赤と青を外へずらす量")]
        [AnimationSlider("F0", "px", 0, 50)]
        [ShowPropertyEditorWhen(nameof(IsBlurEnabled), true)]
        public Animation ColorShift { get; } = new(12, 0, 1000);

        [Display(GroupName = Frames, Name = "にじみの色", Description = "ぼけたとき明るいところからにじむ光の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsBlurEnabled), true)]
        public Color GlowColor { get; set => Set(ref field, value); } = Color.FromRgb(190, 110, 255);

        [Display(GroupName = Frames, Name = "にじみ", Description = "ぼけたとき明るいところからにじむ光の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsBlurEnabled), true)]
        public Animation GlowAmount { get; } = new(60, 0, 1000);

        [Display(GroupName = Frames, Name = "しきい値", Description = "光るところと影になるところの境目の明るさ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsFramesEnabled), true)]
        public Animation Threshold { get; } = new(55, 0, 100);

        [Display(GroupName = Frames, Name = "黒地に白い光", Description = "明るいところだけを光らせ、ほかを黒くした絵")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsFramesEnabled), true)]
        public bool ShowsBurst { get; set => Set(ref field, value, nameof(ShowsBurst), nameof(ShowsBurstColor)); } = true;

        public bool ShowsBurstColor => IsFramesEnabled && ShowsBurst;

        [Display(GroupName = Frames, Name = "光の色", Description = "黒地に白い光の絵で、光の縁に付く色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(ShowsBurstColor), true)]
        public Color BurstColor { get; set => Set(ref field, value); } = Color.FromRgb(255, 200, 90);

        [Display(GroupName = Frames, Name = "白黒反転", Description = "明るさを反転し、白地に黒い影を出した絵")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsFramesEnabled), true)]
        public bool ShowsInverted { get; set => Set(ref field, value, nameof(ShowsInverted), nameof(ShowsRimColor)); } = true;

        public bool ShowsRimColor => IsFramesEnabled && ShowsInverted;

        [Display(GroupName = Frames, Name = "影の縁の色", Description = "白黒反転の絵で、影の縁ににじむ色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(ShowsRimColor), true)]
        public Color RimColor { get; set => Set(ref field, value); } = Color.FromRgb(40, 90, 255);

        [Display(GroupName = Frames, Name = "白飛び", Description = "真っ白に飛ばした絵")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsFramesEnabled), true)]
        public bool ShowsWhiteOut { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Frames, Name = "ネオンの輪郭", Description = "黒地に輪郭だけを光らせた絵")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsFramesEnabled), true)]
        public bool ShowsNeon { get; set => Set(ref field, value, nameof(ShowsNeon), nameof(ShowsNeonLook)); } = true;

        public bool ShowsNeonLook => IsFramesEnabled && ShowsNeon;

        [Display(GroupName = Frames, Name = "輪郭の色", Description = "ネオンの輪郭の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(ShowsNeonLook), true)]
        public Color NeonColor { get; set => Set(ref field, value); } = Color.FromRgb(60, 255, 240);

        [Display(GroupName = Frames, Name = "輪郭の太さ", Description = "ネオンの輪郭の太さ")]
        [AnimationSlider("F1", "px", 0, 10)]
        [ShowPropertyEditorWhen(nameof(ShowsNeonLook), true)]
        public Animation NeonWidth { get; } = new(3, 0, 1000);

        [Display(GroupName = Shock, Name = "大きさ", Description = "広がりきったときの輪の半径")]
        [AnimationSlider("F0", "px", 0, 2000)]
        [ShowPropertyEditorWhen(nameof(IsShockEnabled), true)]
        public Animation ShockRadius { get; } = new(600, 0, 100000);

        [Display(GroupName = Shock, Name = "太さ", Description = "輪の太さ")]
        [AnimationSlider("F0", "px", 0, 300)]
        [ShowPropertyEditorWhen(nameof(IsShockEnabled), true)]
        public Animation ShockWidth { get; } = new(50, 0, 100000);

        [Display(GroupName = Shock, Name = "数", Description = "少しずつ遅れて広がる輪の数")]
        [TextBoxSlider("F0", "", 1, ImpactPattern.MostRings)]
        [Range(1, ImpactPattern.MostRings)]
        [ShowPropertyEditorWhen(nameof(IsShockEnabled), true)]
        public int ShockCount { get; set => Set(ref field, Math.Clamp(value, 1, ImpactPattern.MostRings)); } = 2;

        [Display(GroupName = Shock, Name = "色", Description = "輪の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsShockEnabled), true)]
        public Color ShockColor { get; set => Set(ref field, value); } = Colors.White;

        [Display(GroupName = Shock, Name = "明るさ", Description = "輪の明るさ")]
        [AnimationSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsShockEnabled), true)]
        public Animation ShockStrength { get; } = new(100, 0, 1000);

        [Display(GroupName = Lines, Name = "本数", Description = "線の本数")]
        [TextBoxSlider("F0", "", ImpactPattern.FewestStrokes, 128)]
        [Range(ImpactPattern.FewestStrokes, ImpactPattern.MostStrokes)]
        [ShowPropertyEditorWhen(nameof(IsLinesEnabled), true)]
        public int LineCount { get; set => Set(ref field, Math.Clamp(value, ImpactPattern.FewestStrokes, ImpactPattern.MostStrokes)); } = 40;

        [Display(GroupName = Lines, Name = "飛ぶ距離", Description = "線が消えるまでに進む距離")]
        [AnimationSlider("F0", "px", 0, 2000)]
        [ShowPropertyEditorWhen(nameof(IsLinesEnabled), true)]
        public Animation LineReach { get; } = new(500, 0, 100000);

        [Display(GroupName = Lines, Name = "長さ", Description = "1 本の線の長さ")]
        [AnimationSlider("F0", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsLinesEnabled), true)]
        public Animation LineLength { get; } = new(350, 0, 100000);

        [Display(GroupName = Lines, Name = "太さ", Description = "線のいちばん太いところの幅")]
        [AnimationSlider("F1", "px", 0, 50)]
        [ShowPropertyEditorWhen(nameof(IsLinesEnabled), true)]
        public Animation LineWidth { get; } = new(14, 0, 10000);

        [Display(GroupName = Lines, Name = "立体に広げる", Description = "線を面から外れた向きにも飛ばし、オフなら面の上に並べます")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsLinesEnabled), true)]
        public bool LinesSpread { get; set => Set(ref field, value); }

        [Display(GroupName = Lines, Name = "色", Description = "線の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsLinesEnabled), true)]
        public Color LineColor { get; set => Set(ref field, value); } = Colors.White;

        [Display(GroupName = Lines, Name = "明るさ", Description = "線の明るさ")]
        [AnimationSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsLinesEnabled), true)]
        public Animation LineStrength { get; } = new(100, 0, 1000);

        [Display(GroupName = Flash, Name = "大きさ", Description = "光の玉の半径")]
        [AnimationSlider("F0", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsFlashEnabled), true)]
        public Animation FlashSize { get; } = new(350, 0, 100000);

        [Display(GroupName = Flash, Name = "色", Description = "光の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsFlashEnabled), true)]
        public Color FlashColor { get; set => Set(ref field, value); } = Color.FromRgb(255, 244, 214);

        [Display(GroupName = Flash, Name = "明るさ", Description = "光の明るさ")]
        [AnimationSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsFlashEnabled), true)]
        public Animation FlashStrength { get; } = new(100, 0, 1000);

        [Display(GroupName = Flash, Name = "周りを照らす", Description = "光った瞬間だけ、周りの物を照らす光源になります")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsFlashEnabled), true)]
        public bool LightsScene { get; set => Set(ref field, value, nameof(LightsScene), nameof(IsLighting)); } = true;

        public bool IsLighting => IsFlashEnabled && LightsScene;

        [Display(GroupName = Flash, Name = "照らす距離", Description = "光が届く距離")]
        [AnimationSlider("F0", "px", 0, 5000)]
        [ShowPropertyEditorWhen(nameof(IsLighting), true)]
        public Animation LightReach { get; } = new(1500, 0, 1000000);

        [Display(GroupName = Sparks, Name = "数", Description = "火花の粒の数")]
        [TextBoxSlider("F0", "", ImpactPattern.FewestStrokes, 256)]
        [Range(ImpactPattern.FewestStrokes, ImpactPattern.MostStrokes)]
        [ShowPropertyEditorWhen(nameof(IsSparksEnabled), true)]
        public int SparkCount { get; set => Set(ref field, Math.Clamp(value, ImpactPattern.FewestStrokes, ImpactPattern.MostStrokes)); } = 60;

        [Display(GroupName = Sparks, Name = "飛ぶ距離", Description = "火花がいちばん遠くまで飛ぶ距離")]
        [AnimationSlider("F0", "px", 0, 2000)]
        [ShowPropertyEditorWhen(nameof(IsSparksEnabled), true)]
        public Animation SparkReach { get; } = new(450, 0, 100000);

        [Display(GroupName = Sparks, Name = "大きさ", Description = "火花の粒の太さ")]
        [AnimationSlider("F1", "px", 0, 30)]
        [ShowPropertyEditorWhen(nameof(IsSparksEnabled), true)]
        public Animation SparkSize { get; } = new(7, 0, 10000);

        [Display(GroupName = Sparks, Name = "落ちる量", Description = "消えるまでに重さで下へ落ちる距離")]
        [AnimationSlider("F0", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsSparksEnabled), true)]
        public Animation SparkDrop { get; } = new(200, -100000, 100000);

        [Display(GroupName = Sparks, Name = "色", Description = "火花の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSparksEnabled), true)]
        public Color SparkColor { get; set => Set(ref field, value); } = Color.FromRgb(255, 176, 72);

        [Display(GroupName = Sparks, Name = "明るさ", Description = "火花の明るさ")]
        [AnimationSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsSparksEnabled), true)]
        public Animation SparkStrength { get; } = new(100, 0, 1000);

        public bool IsLightEnabled => IsLighting;

        internal ImpactMoment? FindMoment(in FrameContext time)
        {
            var lead = IsBlurEnabled ? BuildUp : 0;
            var since = (double)time.Frame / time.Fps - Start + lead;

            if (since < 0)
                return null;

            var round = 0;

            if (Interval > 0)
            {
                round = (int)(since / Interval);
                since -= round * Interval;
            }

            var elapsed = since - lead;

            if (elapsed >= Math.Max(Duration, FramesLength(time.Fps)))
                return null;

            return new ImpactMoment((float)(elapsed / Duration), (float)elapsed, Seed + round * RoundSeedStep);
        }

        internal ImpactFrameLook? GetFrameLook(in ImpactMoment moment, in FrameContext time, in ImpactAim aim)
        {
            if (moment.Seconds < 0)
            {
                if (!IsBlurEnabled)
                    return null;

                var ramp = 1f + moment.Seconds / (float)BuildUp;
                var eased = ramp * ramp;

                return new ImpactFrameLook(ImpactFrameKind.Blur, GlowColor.ToVector3(), BlurAmount.GetFloat(time) * eased, ColorShift.GetFloat(time) * eased, GlowAmount.GetFloat(time) / 100f * eased, 0f, 0f, GetArea(time, 1f), aim);
            }

            if (!IsFramesEnabled)
                return null;

            var stages = FrameStages();
            var passed = moment.Seconds * time.Fps / KomaLength;
            var index = (int)passed;

            if (index >= stages.Count)
                return null;

            var kind = stages[index];
            var tint = kind switch
            {
                ImpactFrameKind.Burst => BurstColor,
                ImpactFrameKind.Inverted => RimColor,
                ImpactFrameKind.Neon => NeonColor,
                _ => Colors.White,
            };
            var opened = AreaExpands ? (index + 1f) / stages.Count : 1f;

            return new ImpactFrameLook(kind, tint.ToVector3(), 0f, 0f, 0f, Threshold.GetFloat(time) / 100f, NeonWidth.GetFloat(time), GetArea(time, opened), aim);
        }

        private ImpactFrameArea GetArea(in FrameContext time, float opened)
        {
            if (!IsAroundCenter)
                return ImpactFrameArea.Whole;

            var rest = 1f - opened;

            return new ImpactFrameArea(AreaRadius.GetFloat(time) * (1f - rest * rest * 0.8f), AreaSoftness.GetFloat(time));
        }

        internal float GetFrameSpread(in FrameContext time)
            => WorldScale.ToWorld((IsBlurEnabled ? BlurAmount.GetFloat(time) + ColorShift.GetFloat(time) : 0f) + (ShowsNeonLook ? NeonWidth.GetFloat(time) : 0f));

        private double FramesLength(int fps) => IsFramesEnabled ? (double)FrameStages().Count * KomaLength / fps : 0;

        private List<ImpactFrameKind> FrameStages()
        {
            var stages = new List<ImpactFrameKind>(4);

            if (ShowsBurst)
                stages.Add(ImpactFrameKind.Burst);

            if (ShowsInverted)
                stages.Add(ImpactFrameKind.Inverted);

            if (ShowsWhiteOut)
                stages.Add(ImpactFrameKind.WhiteOut);

            if (ShowsNeon)
                stages.Add(ImpactFrameKind.Neon);

            return stages;
        }

        internal Vector3 GetCenter(in FrameContext time) => WorldScale.ToWorldPosition(CenterX.GetFloat(time), CenterY.GetFloat(time), CenterZ.GetFloat(time));

        internal float GetAngle(in FrameContext time) => IsAimed ? Angle.GetFloat(time) * MathF.PI / 180f : 0f;

        internal float GetSpread(in FrameContext time) => IsAimed ? Math.Clamp(Spread.GetFloat(time), 0f, 360f) * MathF.PI / 180f : MathF.Tau;

        internal float GetReach(in FrameContext time)
        {
            var reach = 0f;

            if (IsShockEnabled)
                reach = MathF.Max(reach, ShockRadius.GetFloat(time) + ShockWidth.GetFloat(time));

            if (IsLinesEnabled)
                reach = MathF.Max(reach, LineReach.GetFloat(time) * (1f + ImpactPattern.LineStartSpan) + LineLength.GetFloat(time) + LineWidth.GetFloat(time));

            if (IsFlashEnabled)
                reach = MathF.Max(reach, FlashSize.GetFloat(time) * ImpactPattern.LongestFlash);

            if (IsSparksEnabled)
                reach = MathF.Max(reach, SparkReach.GetFloat(time) + MathF.Abs(SparkDrop.GetFloat(time)) + SparkSize.GetFloat(time));

            return WorldScale.ToWorld(reach);
        }

        internal float GetShake(in FrameContext time) => IsShakeEnabled ? WorldScale.ToWorld(ShakeAmount.GetFloat(time)) : 0f;

        internal static float FlashFade(float progress)
        {
            var rest = 1f - Math.Clamp(progress / FlashSpan, 0f, 1f);

            return rest * rest;
        }

        internal static float FlashGrowth(float progress)
        {
            var rest = 1f - Math.Clamp(progress / FlashSpan, 0f, 1f);

            return 0.6f + 0.4f * (1f - rest * rest * rest);
        }

        public SceneLight GetLight(in FrameContext itemTime, in Matrix4x4 placement)
        {
            if (FindMoment(itemTime) is not { IsStriking: true } moment)
                return SceneLight.Point(Vector3.Zero, Vector3.Zero, 1f);

            var glow = FlashFade(moment.Progress) * FlashStrength.GetFloat(itemTime) / 100f;
            var reach = WorldScale.ToWorld(LightReach.GetFloat(itemTime)) * ImpactAnchor.ScaleOf(placement);

            return SceneLight.Point(Vector3.Transform(GetCenter(itemTime), placement), FlashColor.ToVector3() * glow, reach);
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new FrameImpact3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
        [
            CenterX, CenterY, CenterZ, Angle, Spread,
            AreaRadius, AreaSoftness,
            BlurAmount, ColorShift, GlowAmount,
            Threshold, NeonWidth,
            ShockRadius, ShockWidth, ShockStrength,
            LineReach, LineLength, LineWidth, LineStrength,
            FlashSize, FlashStrength, LightReach,
            ShakeAmount, ShakeSpeed,
            SparkReach, SparkSize, SparkDrop, SparkStrength,
            CameraSyncAnimation,
        ];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
