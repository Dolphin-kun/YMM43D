using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Project.Shape
{
    internal sealed class Shape3DParameter : ShapeParameter3DBase
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Paint = "色";

        [Display(GroupName = Body, Name = "形", Description = "何を描くか")]
        [EnumComboBox]
        public SolidKind Solid
        {
            get => solid;
            set
            {
                Set(ref solid, value);
                OnPropertyChanged(nameof(GroupCount));
                OnPropertyChanged(nameof(IsCurved));
                OnPropertyChanged(nameof(IsTorus));
            }
        }
        private SolidKind solid = SolidKind.Cube;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡し。六面体では一辺の長さ")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Size { get; } = new(100, 0, 100000);

        [Display(GroupName = Body, Name = "分割の細かさ",
            Description = "丸い形を何枚の面で作るか。大きいほど滑らかで、そのぶん重くなります")]
        [TextBoxSlider("F0", "", Primitives.MinSegments, 64)]
        [ShowPropertyEditorWhen(nameof(IsCurved), true)]
        public int Segments
        {
            get => segments;
            set => Set(ref segments, Math.Clamp(value, Primitives.MinSegments, Primitives.MaxSegments));
        }
        private int segments = Solids.DefaultSegments;

        [Display(GroupName = Body, Name = "太さ", Description = "輪の太さ。100 にすると穴が無くなります")]
        [TextBoxSlider("F0", "%", 5, 100)]
        [ShowPropertyEditorWhen(nameof(IsTorus), true)]
        public int Thickness
        {
            get => thickness;
            set => Set(ref thickness, Math.Clamp(value, 1, 100));
        }
        private int thickness = Solids.DefaultThickness;

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

        [Display(GroupName = Paint, Name = "陰影をつけない",
            Description = "光源を無視して、指定した色のまま塗ります")]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        [Display(GroupName = Paint, Name = "色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.Solid)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        [Display(AutoGenerateField = true)]
        public ImmutableList<FaceColor> FaceColors { get => faceColors; set => Set(ref faceColors, value); }
        private ImmutableList<FaceColor> faceColors = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public ImmutableList<Color> StoredFaceColors { get => storedFaceColors; set => Set(ref storedFaceColors, value); }
        private ImmutableList<Color> storedFaceColors = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsCurved => Solids.IsCurved(Solid);

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsTorus => Solid == SolidKind.Torus;

        internal int GroupCount => Solids.GroupCountOf(Solid);

        internal IReadOnlyList<Color> ResolvedColors => Fill == CubeFill.Solid
            ? [Color]
            : FaceColors.Count > 0 ? [.. FaceColors.Select(f => f.Color)] : StoredFaceColors;

        public Shape3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
            SubscribeChildUndoRedoable(FaceColors);
        }

        public Shape3DParameter() : this(null)
        {
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            SyncFaceColors();
        }

        private void SyncFaceColors()
        {
            if (Fill == CubeFill.Solid)
            {
                if (FaceColors.Count == 0)
                    return;

                StoredFaceColors = [.. FaceColors.Select(f => f.Color)];
                FaceColors = [];
                return;
            }

            var current = FaceColors.Count > 0
                ? [.. FaceColors.Select(f => f.Color)]
                : StoredFaceColors;
            var wanted = Resize(GroupCount, current);

            if (!wanted.SequenceEqual(FaceColors.Select(f => f.Color)))
                FaceColors = [.. wanted.Select(c => new FaceColor { Color = c })];

            if (!wanted.SequenceEqual(StoredFaceColors))
                StoredFaceColors = wanted;
        }

        private static ImmutableList<Color> Resize(int count, ImmutableList<Color> current)
        {
            if (current.Count == count)
                return current;

            if (current.Count > count)
                return current.GetRange(0, count);

            var grown = current.ToBuilder();
            for (var i = current.Count; i < count; i++)
                grown.Add(DefaultColorAt(i, count));

            return grown.ToImmutable();
        }

        private static Color DefaultColorAt(int face, int count)
        {
            var level = (byte)(0x60 + 0x9F * face / Math.Max(1, count - 1));

            return Color.FromRgb(level, level, level);
        }

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
            public Animation Size { get; } = new(100, 0, 100000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public SolidKind Solid { get; set; }
            public int Segments { get; set; }
            public int Thickness { get; set; }
            public CubeFill Fill { get; set; }
            public Color Color { get; set; }
            public ImmutableList<Color> FaceColors { get; set; } = [];
            public ImmutableList<Color> StoredFaceColors { get; set; } = [];

            public SharedData(Shape3DParameter parameter)
            {
                Size.CopyFrom(parameter.Size);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Solid = parameter.Solid;
                Segments = parameter.Segments;
                Thickness = parameter.Thickness;
                Fill = parameter.Fill;
                Color = parameter.Color;
                FaceColors = [.. parameter.FaceColors.Select(f => f.Color)];
                StoredFaceColors = parameter.StoredFaceColors;
            }

            public void CopyTo(Shape3DParameter parameter)
            {
                parameter.Size.CopyFrom(Size);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.Solid = Solid;
                parameter.Segments = Segments;
                parameter.Thickness = Thickness;
                parameter.Fill = Fill;
                parameter.Color = Color;
                parameter.FaceColors = [.. FaceColors.Select(c => new FaceColor { Color = c })];
                parameter.StoredFaceColors = StoredFaceColors;
            }
        }
    }

    public class FaceColor : Animatable
    {
        [Display(GroupName = "色", Name = "面", Description = "上から順に、面の並びと同じ順です")]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }

    public enum CubeFill
    {
        [Display(Name = "単色", Description = "すべての面を同じ色で塗ります")]
        Solid,

        [Display(Name = "面ごと", Description = "面それぞれに色を指定します")]
        PerFace,
    }
}
