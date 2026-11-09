using System.Collections.Immutable;
using System.Numerics;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Graphics.Meshes;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Project.Shape
{
    internal sealed class Shape3DParameter : ShapeParameter3DBase, ISceneRotation3D
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Free = "自由な形";
        private const string Paint = "色";

        [Display(GroupName = Body, Name = "形", Description = "何を描くか。種類ごとに分かれています")]
        [SolidPicker]
        public SolidKind Solid
        {
            get => solid;
            set
            {
                Set(ref solid, value);
                OnPropertyChanged(nameof(GroupCount));
                OnPropertyChanged(nameof(IsCurved));
                OnPropertyChanged(nameof(IsTorus));
                OnPropertyChanged(nameof(IsCustom));
                OnPropertyChanged(nameof(IsExtruded));
                plain = null;
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
            set
            {
                Set(ref segments, Math.Clamp(value, Primitives.MinSegments, Primitives.MaxSegments));
                plain = null;
            }
        }
        private int segments = Solids.DefaultSegments;

        [Display(GroupName = Body, Name = "太さ", Description = "輪の太さ。100 にすると穴が無くなります")]
        [TextBoxSlider("F0", "%", 5, 100)]
        [ShowPropertyEditorWhen(nameof(IsTorus), true)]
        public int Thickness
        {
            get => thickness;
            set
            {
                Set(ref thickness, Math.Clamp(value, 1, 100));
                plain = null;
            }
        }
        private int thickness = Solids.DefaultThickness;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public ImmutableList<double> MeshPoints
        {
            get => meshPoints;
            set { Set(ref meshPoints, value ?? []); Forget(); }
        }
        private ImmutableList<double> meshPoints = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public ImmutableList<int> MeshFaces
        {
            get => meshFaces;
            set { Set(ref meshFaces, value ?? []); Forget(); }
        }
        private ImmutableList<int> meshFaces = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public ImmutableList<int> MeshSides
        {
            get => meshSides;
            set { Set(ref meshSides, value ?? []); Forget(); }
        }
        private ImmutableList<int> meshSides = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public ImmutableList<int> MeshGroups
        {
            get => meshGroups;
            set { Set(ref meshGroups, value ?? []); Forget(); }
        }
        private ImmutableList<int> meshGroups = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public ImmutableList<int> MeshSmooth
        {
            get => meshSmooth;
            set { Set(ref meshSmooth, value ?? []); Forget(); }
        }
        private ImmutableList<int> meshSmooth = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasMesh => MeshSides.Count > 0 && MeshPoints.Count >= 9;

        private MeshTopology? edited;
        private MeshTopology? plain;
        private SurfaceGeometry? baked;

        private void Forget()
        {
            edited = null;
            baked = null;

            OnPropertyChanged(nameof(HasMesh));
        }

        internal MeshTopology ShownMesh()
        {
            if (HasMesh)
                return edited ??= ReadMesh();

            return plain ??= MeshTopology.FromGeometry(Solids.Get(Solid, Segments, Thickness));
        }

        internal SurfaceGeometry EditedGeometry() => baked ??= ShownMesh().ToGeometry();

        internal MeshTopology StoreMesh(MeshTopology built)
        {
            var points = new List<double>(built.Points.Length * 3);

            foreach (var point in built.Points)
            {
                points.Add(point.X);
                points.Add(point.Y);
                points.Add(point.Z);
            }

            var corners = new List<int>();
            var sides = new List<int>();

            foreach (var face in built.Faces)
            {
                corners.AddRange(face);
                sides.Add(face.Length);
            }

            MeshPoints = [.. points];
            MeshFaces = [.. corners];
            MeshSides = [.. sides];
            MeshGroups = [.. built.Groups];
            MeshSmooth = [.. built.Smooth.Select(one => one ? 1 : 0)];

            edited = built;
            baked = null;

            return built;
        }

        internal MeshTopology StorePoints(MeshTopology built)
        {
            var points = new List<double>(built.Points.Length * 3);

            foreach (var point in built.Points)
            {
                points.Add(point.X);
                points.Add(point.Y);
                points.Add(point.Z);
            }

            MeshPoints = [.. points];

            edited = built;
            baked = null;

            return built;
        }

        internal MeshTopology EnsureMesh() => HasMesh ? ShownMesh() : StoreMesh(ShownMesh());

        internal void DropMesh()
        {
            plain = null;

            if (!HasMesh)
                return;

            MeshPoints = [];
            MeshFaces = [];
            MeshSides = [];
            MeshGroups = [];
            MeshSmooth = [];
        }

        private MeshTopology ReadMesh()
        {
            var points = new Vector3[MeshPoints.Count / 3];

            for (var i = 0; i < points.Length; i++)
            {
                points[i] = new Vector3(
                    (float)MeshPoints[i * 3], (float)MeshPoints[i * 3 + 1], (float)MeshPoints[i * 3 + 2]);
            }

            var faces = new int[MeshSides.Count][];
            var groups = new int[faces.Length];
            var smooth = new bool[faces.Length];
            var at = 0;

            for (var f = 0; f < faces.Length; f++)
            {
                var sides = Math.Clamp(MeshSides[f], 3, 256);
                var corners = new int[sides];

                for (var i = 0; i < sides; i++)
                {
                    corners[i] = at + i < MeshFaces.Count
                        ? Math.Clamp(MeshFaces[at + i], 0, Math.Max(points.Length - 1, 0))
                        : 0;
                }

                at += sides;
                faces[f] = corners;
                groups[f] = f < MeshGroups.Count ? MeshGroups[f] : 0;
                smooth[f] = f < MeshSmooth.Count && MeshSmooth[f] != 0;
            }

            return new MeshTopology(points, faces, groups, smooth, GroupCount);
        }

        [Display(GroupName = Free, Name = "作り方",
            Description = "下書きの形を、そのまま奥行きへ押し出すか、縦の軸のまわりに回すか")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsCustom), true)]
        public OutlineForm Form
        {
            get => form;
            set
            {
                Set(ref form, value);
                OnPropertyChanged(nameof(GroupCount));
                OnPropertyChanged(nameof(IsCurved));
                OnPropertyChanged(nameof(IsExtruded));
            }
        }
        private OutlineForm form = OutlineForm.Extrude;

        [Display(GroupName = Free, Name = "厚み",
            Description = "押し出す奥行き。100% で、下書きのいちばん長い差し渡しと同じ奥行きになります")]
        [TextBoxSlider("F0", "%", 1, 300)]
        [ShowPropertyEditorWhen(nameof(IsExtruded), true)]
        public int Depth { get => depth; set => Set(ref depth, Math.Clamp(value, 1, 1000)); }
        private int depth = 50;

        [Display(GroupName = Free, Name = "点の数",
            Description = "増やすと、いちばん長い辺の真ん中に点が足されます。減らすと終わりの点から消えます")]
        [TextBoxSlider("F0", "個", OutlineSolid.MinPoints, 24)]
        [ShowPropertyEditorWhen(nameof(IsCustom), true)]
        public int PointCount
        {
            get => pointCount;
            set => Set(ref pointCount, Math.Clamp(value, OutlineSolid.MinPoints, OutlineSolid.MaxPoints));
        }
        private int pointCount = 4;

        [Display(AutoGenerateField = true)]
        [ShowPropertyEditorWhen(nameof(IsCustom), true)]
        public ImmutableList<OutlinePoint> Outline { get => outline; set => Set(ref outline, value); }
        private ImmutableList<OutlinePoint> outline = [];

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsCustom => Solid == SolidKind.Custom;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsExtruded => IsCustom && Form == OutlineForm.Extrude;

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
        public CubeFill Fill
        {
            get => fill;
            set
            {
                Set(ref fill, value);
                OnPropertyChanged(nameof(IsSolidColor));
            }
        }
        private CubeFill fill = CubeFill.Solid;

        [Display(GroupName = Paint, Name = "陰影", Description = "光源にしたがって陰影をつけます。切ると、指定した色のまま塗ります")]
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
            }
        }
        private bool isUnlit;

        [Display(GroupName = Paint, Name = "つや",
            Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Paint, Name = "つやの鋭さ",
            Description = "大きいほど映り込みが小さく締まり、磨いたように見えます")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        internal SurfaceGloss GetGloss(in FrameContext time)
            => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        [Display(GroupName = Paint, Name = "色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSolidColor), true)]
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
        public bool IsSolidColor => Fill == CubeFill.Solid;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsCurved => Solids.IsCurved(Solid) || (IsCustom && Form == OutlineForm.Lathe);

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsTorus => Solid == SolidKind.Torus;

        internal int GroupCount => IsCustom ? OutlineSolid.GroupCountOf(Form) : Solids.GroupCountOf(Solid);

        internal IReadOnlyList<Color> ResolvedColors => Fill == CubeFill.Solid
            ? [Color]
            : FaceColors.Count > 0 ? [.. FaceColors.Select(f => f.Color)] : StoredFaceColors;

        public Shape3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
            SubscribeChildUndoRedoable(FaceColors);
            SubscribeChildUndoRedoable(Outline);
        }

        public Shape3DParameter() : this(null)
        {
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            SyncOutline();
            SyncFaceColors();
        }

        internal Matrix4x4 GetLocalMatrix(in FrameContext time)
            => Matrix4x4.CreateScale(WorldScale.ToWorld(Size.GetFloat(time)))
             * Rotation3D.ForObject(
                   RotationX.GetFloat(time), RotationY.GetFloat(time), RotationZ.GetFloat(time));

        internal Vector2[] OutlinePoints(in FrameContext time)
        {
            var points = new Vector2[Outline.Count];

            for (var i = 0; i < points.Length; i++)
                points[i] = new Vector2(Outline[i].X.GetFloat(time), Outline[i].Y.GetFloat(time));

            return points;
        }

        private static readonly FrameContext Start = new(0, 1, 30);

        private void SyncOutline()
        {
            if (!IsCustom)
                return;

            var wanted = Math.Clamp(PointCount, OutlineSolid.MinPoints, OutlineSolid.MaxPoints);

            if (Outline.Count < OutlineSolid.MinPoints)
            {
                Outline = [.. Ring(wanted).Select(point => new OutlinePoint(point.X, point.Y))];
                SubscribeChildUndoRedoable(Outline);

                return;
            }

            if (Outline.Count == wanted)
                return;

            var points = Outline.ToList();

            while (points.Count > wanted)
                points.RemoveAt(points.Count - 1);

            while (points.Count < wanted)
            {
                var edge = LongestEdge(points);
                var from = points[edge];
                var to = points[(edge + 1) % points.Count];

                points.Insert(
                    edge + 1,
                    new OutlinePoint(
                        (from.X.GetValue(Start) + to.X.GetValue(Start)) / 2,
                        (from.Y.GetValue(Start) + to.Y.GetValue(Start)) / 2));
            }

            Outline = [.. points];
            SubscribeChildUndoRedoable(Outline);
        }

        private static int LongestEdge(List<OutlinePoint> points)
        {
            var found = 0;
            var longest = -1d;

            for (var i = 0; i < points.Count; i++)
            {
                var from = points[i];
                var to = points[(i + 1) % points.Count];

                var alongX = from.X.GetValue(Start) - to.X.GetValue(Start);
                var alongY = from.Y.GetValue(Start) - to.Y.GetValue(Start);
                var span = alongX * alongX + alongY * alongY;

                if (span <= longest)
                    continue;

                longest = span;
                found = i;
            }

            return found;
        }

        internal const float DefaultRadius = 50f;

        public static IReadOnlyList<Vector2> Ring(int count)
        {
            var points = new Vector2[Math.Max(count, OutlineSolid.MinPoints)];
            var start = MathF.PI / 2f + MathF.PI / points.Length;

            for (var i = 0; i < points.Length; i++)
            {
                var angle = start + MathF.Tau * i / points.Length;

                points[i] = new Vector2(
                    MathF.Round(DefaultRadius * MathF.Cos(angle), 2),
                    MathF.Round(DefaultRadius * MathF.Sin(angle), 2));
            }

            return points;
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
            => [Size, RotationX, RotationY, RotationZ,
                Gloss, GlossSharpness, CameraSyncAnimation, .. Outline];

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
            public Animation Gloss { get; } = new(0, 0, 1000);
            public Animation GlossSharpness { get; } = new(50, 0, 100);
            public SolidKind Solid { get; set; }
            public int Segments { get; set; }
            public int Thickness { get; set; }
            public CubeFill Fill { get; set; }
            public OutlineForm Form { get; set; }
            public int Depth { get; set; } = 50;
            public int PointCount { get; set; } = 4;
            public bool IsUnlit { get; set; }
            public Color Color { get; set; }
            public ImmutableList<Color> FaceColors { get; set; } = [];
            public ImmutableList<Color> StoredFaceColors { get; set; } = [];

            public SharedData(Shape3DParameter parameter)
            {
                Size.CopyFrom(parameter.Size);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Gloss.CopyFrom(parameter.Gloss);
                GlossSharpness.CopyFrom(parameter.GlossSharpness);
                Solid = parameter.Solid;
                Segments = parameter.Segments;
                Thickness = parameter.Thickness;
                Form = parameter.Form;
                Depth = parameter.Depth;
                PointCount = parameter.PointCount;
                Fill = parameter.Fill;
                IsUnlit = parameter.IsUnlit;
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
                parameter.Gloss.CopyFrom(Gloss);
                parameter.GlossSharpness.CopyFrom(GlossSharpness);
                parameter.Solid = Solid;
                parameter.Segments = Segments;
                parameter.Thickness = Thickness;
                parameter.Form = Form;
                parameter.Depth = Depth;
                parameter.PointCount = PointCount;
                parameter.Fill = Fill;
                parameter.IsUnlit = IsUnlit;
                parameter.Color = Color;
                parameter.FaceColors = [.. FaceColors.Select(c => new FaceColor { Color = c })];
                parameter.StoredFaceColors = StoredFaceColors;
            }
        }
    }

    public class OutlinePoint : Animatable
    {
        [Display(GroupName = "自由な形", Name = "点", Description = "下書きの点の、横の位置です")]
        [AnimationSlider("F1", "px", -300, 300)]
        public Animation X { get; }

        [Display(GroupName = "自由な形", Name = "", Description = "下書きの点の、縦の位置です")]
        [AnimationSlider("F1", "px", -300, 300)]
        public Animation Y { get; }

        public OutlinePoint() : this(0, 0)
        {
        }

        public OutlinePoint(double x, double y)
        {
            X = new Animation(x, -100000, 100000);
            Y = new Animation(y, -100000, 100000);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [X, Y];
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
