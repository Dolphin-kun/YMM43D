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

namespace Noise3D
{
    public sealed class Noise3DParameter(SharedDataStore? sharedData) : ShapeParameter3DBase(sharedData), ISceneRotation3D
    {
        public const int MinSlices = 8;
        public const int MaxSlices = 256;

        private const string Noise = "ノイズ";
        private const string Volume = "範囲";
        private const string Rotation = "3D回転";
        private const string Tone = "濃淡";
        private const string Fractal = "フラクタル";
        private const string Warp = "歪み";
        private const string Transform = "変形";
        private const string Movement = "移動";

        private const float BasePixels = 200f;

        [Display(GroupName = Noise, Name = "種類", Description = "ノイズの種類")]
        [EnumComboBox]
        public Noise3DType NoiseType { get; set => Set(ref field, value); } = Noise3DType.Perlin;

        [Display(GroupName = Noise, Name = "シード値", Description = "模様を決める乱数の種")]
        [TextBoxSlider("F0", "", 0, 1000)]
        [Range(0, int.MaxValue)]
        public int Seed { get; set => Set(ref field, Math.Max(0, value)); }

        [Display(GroupName = Volume, Name = "幅", Description = "ノイズを出す箱の X 方向の大きさ")]
        [AnimationSlider("F1", "px", 0, 2000)]
        public Animation Width { get; } = new(600, 0, 100000);

        [Display(GroupName = Volume, Name = "高さ", Description = "ノイズを出す箱の Y 方向の大きさ")]
        [AnimationSlider("F1", "px", 0, 2000)]
        public Animation Height { get; } = new(300, 0, 100000);

        [Display(GroupName = Volume, Name = "奥行き", Description = "ノイズを出す箱の Z 方向の大きさ")]
        [AnimationSlider("F1", "px", 0, 2000)]
        public Animation Depth { get; } = new(600, 0, 100000);

        [Display(GroupName = Volume, Name = "色", Description = "ノイズの色")]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        [Display(GroupName = Volume, Name = "濃さ", Description = "いちばん濃いところを 100px 進んだときに向こうが隠れる度合い")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation Density { get; } = new(25, 0, 10000);

        [Display(GroupName = Volume, Name = "ふちのぼかし", Description = "箱のふちを外へなだらかに薄くします")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation EdgeBlur { get; } = new(30, 0, 100);

        [Display(GroupName = Volume, Name = "分割の細かさ", Description = "箱を切って描く板の枚数の上限")]
        [TextBoxSlider("F0", "枚", MinSlices, MaxSlices)]
        [Range(MinSlices, MaxSlices)]
        public int Slices { get; set => Set(ref field, Math.Clamp(value, MinSlices, MaxSlices)); } = 64;

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Tone, Name = "強さ", Description = "ノイズの強さ")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Strength { get; } = new(100, 0, 100);

        [Display(GroupName = Tone, Name = "しきい値", Description = "これより薄いところを消し、残りを濃く引き伸ばします")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Threshold { get; } = new(45, 0, 100);

        [Display(GroupName = Tone, Name = "階調数", Description = "指定した階調数に減色します")]
        [AnimationSlider("F0", "", 0, 8)]
        public Animation Levels { get; } = new(0, 0, 256);

        [Display(GroupName = Fractal, Name = "繰り返し", Description = "細かいノイズを何段重ねるか")]
        [AnimationSlider("F0", "", 0, 8)]
        public Animation Octaves { get; } = new(4, 0, 8);

        [Display(GroupName = Fractal, Name = "合成方式", Description = "段ごとのノイズの重ね方で、ランダムとブロックでは使いません")]
        [EnumComboBox]
        public Noise3DFractalMode FractalMode { get; set => Set(ref field, value); } = Noise3DFractalMode.Normal;

        [Display(GroupName = Fractal, Name = "周波数倍率", Description = "段ごとに周波数を掛ける倍率")]
        [AnimationSlider("F2", "", 1, 4)]
        public Animation Lacunarity { get; } = new(2, 1, 16);

        [Display(GroupName = Fractal, Name = "振幅減衰", Description = "段ごとに振幅を掛ける倍率")]
        [AnimationSlider("F2", "", 0, 1)]
        public Animation Gain { get; } = new(0.5, 0, 1);

        [Display(GroupName = Warp, Name = "歪み強さ", Description = "ノイズの座標を歪める強さ")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation WarpStrength { get; } = new(0, 0, 100000);

        [Display(GroupName = Warp, Name = "歪みスケール", Description = "歪みに使うノイズの細かさ")]
        [AnimationSlider("F1", "%", 25, 400)]
        public Animation WarpScale { get; } = new(100, 1, 10000);

        [Display(GroupName = Transform, Name = "サイズ", Description = "ノイズ全体の大きさ")]
        [AnimationSlider("F1", "%", 0, 400)]
        public Animation Size { get; } = new(100, 0, 100000);

        [Display(GroupName = Transform, Name = "サイズX", Description = "ノイズの X 方向の大きさ")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation ScaleX { get; } = new(100, 0, 100000);

        [Display(GroupName = Transform, Name = "サイズY", Description = "ノイズの Y 方向の大きさ")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation ScaleY { get; } = new(100, 0, 100000);

        [Display(GroupName = Transform, Name = "サイズZ", Description = "ノイズの Z 方向の大きさ")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation ScaleZ { get; } = new(100, 0, 100000);

        [Display(GroupName = Transform, Name = "回転角", Description = "箱は回さずに、ノイズの模様だけを Z 軸まわりに回します")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation Angle { get; } = new(0, -100000, 100000);

        [Display(GroupName = Movement, Name = "X", Description = "ノイズの模様を X 方向にずらします")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation X { get; } = new(0, -100000, 100000);

        [Display(GroupName = Movement, Name = "Y", Description = "ノイズの模様を Y 方向にずらします")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Y { get; } = new(0, -100000, 100000);

        [Display(GroupName = Movement, Name = "Z", Description = "ノイズの模様を Z 方向にずらします")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Z { get; } = new(0, -100000, 100000);

        [Display(GroupName = Movement, Name = "移動速度X", Description = "ノイズが X 方向に移動する速度")]
        [AnimationSlider("F2", "px/f", -10, 10)]
        public Animation SpeedX { get; } = new(0, -10000, 10000);

        [Display(GroupName = Movement, Name = "移動速度Y", Description = "ノイズが Y 方向に移動する速度")]
        [AnimationSlider("F2", "px/f", -10, 10)]
        public Animation SpeedY { get; } = new(0, -10000, 10000);

        [Display(GroupName = Movement, Name = "移動速度Z", Description = "ノイズが Z 方向に移動する速度")]
        [AnimationSlider("F2", "px/f", -10, 10)]
        public Animation SpeedZ { get; } = new(0, -10000, 10000);

        public Noise3DParameter() : this(null)
        {
        }

        internal Vector3 GetBoxPixels(in FrameContext time) => Vector3.Max(new Vector3(Width.GetFloat(time), Height.GetFloat(time), Depth.GetFloat(time)), Vector3.Zero);

        internal Matrix4x4 GetLocalMatrix(in FrameContext time) => Matrix4x4.CreateScale(GetBoxPixels(time) * WorldScale.ToWorld(1f)) * GetTurn(time);

        internal Vector3 GetOffset(in FrameContext time)
        {
            var position = new Vector3(X.GetFloat(time), Y.GetFloat(time), Z.GetFloat(time));
            var speed = new Vector3(SpeedX.GetFloat(time), SpeedY.GetFloat(time), SpeedZ.GetFloat(time));

            return position + speed * time.Frame;
        }

        internal Vector3 GetFeatureSize(in FrameContext time)
        {
            var scale = new Vector3(ScaleX.GetFloat(time), ScaleY.GetFloat(time), ScaleZ.GetFloat(time)) / 100f;

            return Vector3.Max(scale * (BasePixels * Size.GetFloat(time) / 100f), new Vector3(0.01f));
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices) => new Noise3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Width, Height, Depth, Density, EdgeBlur, RotationX, RotationY, RotationZ, Strength, Threshold, Levels, Octaves, Lacunarity, Gain, WarpStrength, WarpScale, Size, ScaleX, ScaleY, ScaleZ, Angle, X, Y, Z, SpeedX, SpeedY, SpeedZ, CameraSyncAnimation, .. PivotAnimatables];

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store) => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store) => store.Save(new SharedData(this));

        private sealed class SharedData
        {
            public Noise3DType NoiseType { get; set; }
            public int Seed { get; set; }
            public Color Color { get; set; }
            public int Slices { get; set; }
            public Noise3DFractalMode FractalMode { get; set; }
            public Animation Width { get; } = new(600, 0, 100000);
            public Animation Height { get; } = new(300, 0, 100000);
            public Animation Depth { get; } = new(600, 0, 100000);
            public Animation Density { get; } = new(25, 0, 10000);
            public Animation EdgeBlur { get; } = new(30, 0, 100);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public Animation Strength { get; } = new(100, 0, 100);
            public Animation Threshold { get; } = new(45, 0, 100);
            public Animation Levels { get; } = new(0, 0, 256);
            public Animation Octaves { get; } = new(4, 0, 8);
            public Animation Lacunarity { get; } = new(2, 1, 16);
            public Animation Gain { get; } = new(0.5, 0, 1);
            public Animation WarpStrength { get; } = new(0, 0, 100000);
            public Animation WarpScale { get; } = new(100, 1, 10000);
            public Animation Size { get; } = new(100, 0, 100000);
            public Animation ScaleX { get; } = new(100, 0, 100000);
            public Animation ScaleY { get; } = new(100, 0, 100000);
            public Animation ScaleZ { get; } = new(100, 0, 100000);
            public Animation Angle { get; } = new(0, -100000, 100000);
            public Animation X { get; } = new(0, -100000, 100000);
            public Animation Y { get; } = new(0, -100000, 100000);
            public Animation Z { get; } = new(0, -100000, 100000);
            public Animation SpeedX { get; } = new(0, -10000, 10000);
            public Animation SpeedY { get; } = new(0, -10000, 10000);
            public Animation SpeedZ { get; } = new(0, -10000, 10000);

            public SharedData(Noise3DParameter parameter)
            {
                NoiseType = parameter.NoiseType;
                Seed = parameter.Seed;
                Color = parameter.Color;
                Slices = parameter.Slices;
                FractalMode = parameter.FractalMode;

                foreach (var (target, source) in Pairs(this, parameter))
                    target.CopyFrom(source);
            }

            public void CopyTo(Noise3DParameter parameter)
            {
                parameter.NoiseType = NoiseType;
                parameter.Seed = Seed;
                parameter.Color = Color;
                parameter.Slices = Slices;
                parameter.FractalMode = FractalMode;

                foreach (var (source, target) in Pairs(this, parameter))
                    target.CopyFrom(source);
            }

            private static (Animation Shared, Animation Parameter)[] Pairs(SharedData shared, Noise3DParameter parameter) =>
            [
                (shared.Width, parameter.Width), (shared.Height, parameter.Height), (shared.Depth, parameter.Depth),
                (shared.Density, parameter.Density), (shared.EdgeBlur, parameter.EdgeBlur),
                (shared.RotationX, parameter.RotationX), (shared.RotationY, parameter.RotationY), (shared.RotationZ, parameter.RotationZ),
                (shared.Strength, parameter.Strength), (shared.Threshold, parameter.Threshold), (shared.Levels, parameter.Levels),
                (shared.Octaves, parameter.Octaves), (shared.Lacunarity, parameter.Lacunarity), (shared.Gain, parameter.Gain),
                (shared.WarpStrength, parameter.WarpStrength), (shared.WarpScale, parameter.WarpScale),
                (shared.Size, parameter.Size), (shared.ScaleX, parameter.ScaleX), (shared.ScaleY, parameter.ScaleY),
                (shared.ScaleZ, parameter.ScaleZ), (shared.Angle, parameter.Angle),
                (shared.X, parameter.X), (shared.Y, parameter.Y), (shared.Z, parameter.Z),
                (shared.SpeedX, parameter.SpeedX), (shared.SpeedY, parameter.SpeedY), (shared.SpeedZ, parameter.SpeedZ),
            ];
        }
    }
}
