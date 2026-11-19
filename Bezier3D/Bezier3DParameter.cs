using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Controls;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Bezier3D
{
    public sealed class Bezier3DParameter(SharedDataStore? sharedData) : ShapeParameter3DBase(sharedData), ISceneRotation3D, IScenePointHandles
    {
        private const string Line = "";
        private const string Place = "点";
        private const string Paint = "色";
        private const string Edge = "端と断面";
        private const string Rotation = "3D回転";

        [Display(GroupName = Line, Name = "線の種類", Description = "点をどう結ぶか")]
        [EnumComboBox]
        public BezierLineKind LineType { get; set => Set(ref field, value); } = BezierLineKind.Cubic;

        [Display(GroupName = Line, Name = "線のスタイル", Description = "実線か、破線・点線か")]
        [EnumComboBox]
        public BezierDashStyle DashStyle { get; set => Set(ref field, value, nameof(DashStyle), nameof(IsDashed), nameof(IsCustomDash)); } = BezierDashStyle.Solid;

        public bool IsDashed => DashStyle != BezierDashStyle.Solid;

        public bool IsCustomDash => DashStyle == BezierDashStyle.Custom;

        [Display(GroupName = Line, Name = "破線のパターン", Description = "線と空白の長さを、太さの何倍かで「10,2,1,2」のように並べます")]
        [TextEditor(AcceptsReturn = false)]
        [ShowPropertyEditorWhen(nameof(IsCustomDash), true)]
        public string DashPattern { get; set => Set(ref field, value); } = "10,2,1,2";

        [Display(GroupName = Line, Name = "太さ", Description = "線の太さ")]
        [AnimationSlider("F1", "px", 0, 200)]
        public Animation Thickness { get; } = new(24, 0, 100000);

        [Display(GroupName = Line, Name = "長さ", Description = "始点から何%まで描くか")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation LengthRate { get; } = new(100, 0, 100);

        [Display(GroupName = Line, Name = "オフセット", Description = "破線の位置をずらします")]
        [AnimationSlider("F1", "", -100, 100)]
        [ShowPropertyEditorWhen(nameof(IsDashed), true)]
        public Animation DashOffset { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Line, Name = "閉じる", Description = "終点から始点へ戻って輪にします")]
        [ToggleSlider]
        public bool IsClosed { get; set => Set(ref field, value); }

        [Display(GroupName = Place, Name = "", Description = "＋ で点を足し、− で消し、▲▼ で並べ替えます")]
        [ControlPointsEditor]
        public ImmutableList<ControlPoint3D> Points { get; set => Set(ref field, value); } = [new(-300, 0, 0), new(-100, -250, 0), new(100, 250, 0), new(300, 0, 0)];

        [Display(GroupName = Paint, Name = "始点の色", Description = "線の始まりの色")]
        [ColorPicker]
        public Color FromColor { get => fromColor; set => Set(ref fromColor, value); }
        private Color fromColor = Color.FromRgb(0x6E, 0xC6, 0xFF);

        [Display(GroupName = Paint, Name = "終点の色", Description = "始点の色から終点の色へ、線に沿って変わります")]
        [ColorPicker]
        public Color ToColor { get => toColor; set => Set(ref toColor, value); }
        private Color toColor = Color.FromRgb(0xFF, 0x8A, 0xD8);

        [Display(GroupName = Paint, Name = "光を当てない", Description = "陰影を付けずそのままの色で出します")]
        [ToggleSlider]
        public bool IsUnlit { get; set => Set(ref field, value); }

        [Display(GroupName = Paint, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Gloss { get; } = new(30, 0, 1000);

        [Display(GroupName = Paint, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation GlossSharpness { get; } = new(70, 0, 100);

        [Display(GroupName = Edge, Name = "断面", Description = "管にするか帯にするか")]
        [EnumComboBox]
        public BezierSection Section { get; set => Set(ref field, value, nameof(Section), nameof(IsRound)); } = BezierSection.Round;

        public bool IsRound => Section == BezierSection.Round;

        [Display(GroupName = Edge, Name = "始点の形", Description = "始点の端の閉じ方")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsRound), true)]
        public BezierCap StartCap { get; set => Set(ref field, value); } = BezierCap.Round;

        [Display(GroupName = Edge, Name = "終点の形", Description = "終点の端の閉じ方")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsRound), true)]
        public BezierCap EndCap { get; set => Set(ref field, value); } = BezierCap.Round;

        [Display(GroupName = Edge, Name = "破線の端", Description = "破線の切れ目の閉じ方")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsRound), true)]
        public BezierCap DashCap { get; set => Set(ref field, value); } = BezierCap.Round;

        [Display(GroupName = Edge, Name = "先細り", Description = "終点へ向かって細くします")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Taper { get; } = new(0, 0, 100);

        [Display(GroupName = Edge, Name = "長さの分割", Description = "曲線 1 区間を何本に分けるか")]
        [TextBoxSlider("F0", "", BezierPath.LeastSegments, BezierPath.MostSegments)]
        [Range(BezierPath.LeastSegments, BezierPath.MostSegments)]
        public int Segments { get; set => Set(ref field, Math.Clamp(value, BezierPath.LeastSegments, BezierPath.MostSegments)); } = 16;

        [Display(GroupName = Edge, Name = "管の分割", Description = "管の断面を何角形にするか")]
        [TextBoxSlider("F0", "", BezierTube.LeastSides, 32)]
        [Range(BezierTube.LeastSides, BezierTube.MostSides)]
        [ShowPropertyEditorWhen(nameof(IsRound), true)]
        public int Sides { get; set => Set(ref field, Math.Clamp(value, BezierTube.LeastSides, BezierTube.MostSides)); } = 10;

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        public int SelectedHandle { get; set => SetWithoutUndoRedo(ref field, value); }

        public int HandleCount => Points.Count;

        public Bezier3DParameter() : this(null)
        {
        }

        public Matrix4x4 GetHandleTransform(in FrameContext itemTime) => GetLocalMatrix(itemTime);

        public Vector3 GetHandle(int index, in FrameContext itemTime)
        {
            if (index < 0 || index >= Points.Count)
                return Vector3.Zero;

            var point = Points[index];

            return WorldScale.ToWorldPosition(point.X.GetFloat(itemTime), point.Y.GetFloat(itemTime), point.Z.GetFloat(itemTime));
        }

        public void MoveHandle(int index, in Vector3 shift, in FrameContext itemTime, in EditScope scope)
        {
            if (index < 0 || index >= Points.Count)
                return;

            var moved = Matrix4x4.Invert(GetHandleTransform(itemTime), out var inverse) ? Vector3.TransformNormal(shift, inverse) : shift;
            var point = Points[index];

            scope.NudgePosition(point.X, point.Y, point.Z, moved);
        }

        internal Vector3[] GetPoints(in FrameContext time)
        {
            var made = new Vector3[Points.Count];

            for (var i = 0; i < made.Length; i++)
                made[i] = WorldScale.ToWorldPosition(Points[i].X.GetFloat(time), Points[i].Y.GetFloat(time), Points[i].Z.GetFloat(time));

            return made;
        }

        internal float GetRadius(in FrameContext time) => WorldScale.ToWorld(MathF.Max(Thickness.GetFloat(time), 0f)) * 0.5f;

        internal float GetTaper(in FrameContext time) => Math.Clamp(Taper.GetFloat(time) / 100f, 0f, 1f);

        internal float GetLengthRate(in FrameContext time) => Math.Clamp(LengthRate.GetFloat(time) / 100f, 0f, 1f);

        internal float[] GetDashPattern() => DashStyle switch
        {
            BezierDashStyle.Dash => BezierPath.Dash,
            BezierDashStyle.Dot => BezierPath.Dot,
            BezierDashStyle.DashDot => BezierPath.DashDot,
            BezierDashStyle.DashDotDot => BezierPath.DashDotDot,
            BezierDashStyle.Custom => BezierPath.Parse(DashPattern),
            _ => [],
        };

        internal SurfaceGloss GetGloss(in FrameContext time) => SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        internal Matrix4x4 GetLocalMatrix(in FrameContext time) => GetTurn(time);

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices) => new Bezier3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Thickness, LengthRate, DashOffset, Taper, Gloss, GlossSharpness, RotationX, RotationY, RotationZ, CameraSyncAnimation, .. PivotAnimatables, .. Points];

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store) => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store) => store.Save(new SharedData(this));

        private sealed class SharedData
        {
            public Animation Thickness { get; } = new(24, 0, 100000);
            public Animation LengthRate { get; } = new(100, 0, 100);
            public Animation DashOffset { get; } = new(0, -1000000, 1000000);
            public Animation Taper { get; } = new(0, 0, 100);
            public Animation Gloss { get; } = new(30, 0, 1000);
            public Animation GlossSharpness { get; } = new(70, 0, 100);
            public BezierLineKind LineType { get; set; } = BezierLineKind.Cubic;
            public BezierDashStyle DashStyle { get; set; }
            public string DashPattern { get; set; } = "10,2,1,2";
            public bool IsClosed { get; set; }
            public BezierSection Section { get; set; }
            public BezierCap StartCap { get; set; } = BezierCap.Round;
            public BezierCap EndCap { get; set; } = BezierCap.Round;
            public BezierCap DashCap { get; set; } = BezierCap.Round;
            public int Segments { get; set; } = 16;
            public int Sides { get; set; } = 10;
            public Color FromColor { get; set; } = Color.FromRgb(0x6E, 0xC6, 0xFF);
            public Color ToColor { get; set; } = Color.FromRgb(0xFF, 0x8A, 0xD8);
            public bool IsUnlit { get; set; }

            public SharedData()
            {
            }

            public SharedData(Bezier3DParameter parameter)
            {
                Thickness.CopyFrom(parameter.Thickness);
                LengthRate.CopyFrom(parameter.LengthRate);
                DashOffset.CopyFrom(parameter.DashOffset);
                Taper.CopyFrom(parameter.Taper);
                Gloss.CopyFrom(parameter.Gloss);
                GlossSharpness.CopyFrom(parameter.GlossSharpness);
                LineType = parameter.LineType;
                DashStyle = parameter.DashStyle;
                DashPattern = parameter.DashPattern;
                IsClosed = parameter.IsClosed;
                Section = parameter.Section;
                StartCap = parameter.StartCap;
                EndCap = parameter.EndCap;
                DashCap = parameter.DashCap;
                Segments = parameter.Segments;
                Sides = parameter.Sides;
                FromColor = parameter.FromColor;
                ToColor = parameter.ToColor;
                IsUnlit = parameter.IsUnlit;
            }

            public void CopyTo(Bezier3DParameter parameter)
            {
                parameter.Thickness.CopyFrom(Thickness);
                parameter.LengthRate.CopyFrom(LengthRate);
                parameter.DashOffset.CopyFrom(DashOffset);
                parameter.Taper.CopyFrom(Taper);
                parameter.Gloss.CopyFrom(Gloss);
                parameter.GlossSharpness.CopyFrom(GlossSharpness);
                parameter.LineType = LineType;
                parameter.DashStyle = DashStyle;
                parameter.DashPattern = DashPattern;
                parameter.IsClosed = IsClosed;
                parameter.Section = Section;
                parameter.StartCap = StartCap;
                parameter.EndCap = EndCap;
                parameter.DashCap = DashCap;
                parameter.Segments = Segments;
                parameter.Sides = Sides;
                parameter.FromColor = FromColor;
                parameter.ToColor = ToColor;
                parameter.IsUnlit = IsUnlit;
            }
        }
    }
}
