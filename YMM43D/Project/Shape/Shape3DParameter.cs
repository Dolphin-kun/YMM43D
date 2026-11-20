using System.Collections.Immutable;
using System.ComponentModel;
using System.Numerics;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Graphics.Meshes;
using YMM43D.Commons;
using YMM43D.Controls.MaterialSlots;
using YMM43D.Controls.ModifierStack;
using YMM43D.Controls.ShapeBones;
using YMM43D.Controls.ShapeKeys;
using YMM43D.Graphics.Models;
using YMM43D.Plugin;
using YMM43D.Project.Model;
using YMM43D.Project.Shape.Modifiers;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Project.Shape
{
    internal sealed class Shape3DParameter(SharedDataStore? sharedData) : ShapeParameter3DBase(sharedData), ISceneRotation3D, IBoneHost
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Free = "自由な形";
        private const string Paint = "色";
        private const string Modify = "モディファイア";
        private const string Keys = "シェイプキー";
        private const string Rig = "骨";
        private const int AppliedFps = 60;
        private const int SharpBit = 1;
        private const int SeamBit = 2;
        private const float DefaultRadius = 50f;

        [Display(GroupName = Body, Name = "形", Description = "描く立体の種類")]
        [SolidPicker]
        public SolidKind Solid
        {
            get;
            set
            {
                Set(ref field, value, nameof(Solid), nameof(GroupCount), nameof(IsCurved), nameof(IsTorus), nameof(IsCustom), nameof(IsExtruded));
                plain = null;
            }
        } = SolidKind.Cube;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡しの長さで、立方体では一辺の長さ")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Size { get; } = new(100, 0, 100000);

        [Display(GroupName = Body, Name = "分割の細かさ", Description = "丸い形を作る面の細かさで、細かくするほど重くなります")]
        [TextBoxSlider("F0", "", Primitives.MinSegments, 64)]
        [ShowPropertyEditorWhen(nameof(IsCurved), true)]
        public int Segments
        {
            get;
            set
            {
                Set(ref field, Math.Clamp(value, Primitives.MinSegments, Primitives.MaxSegments));
                plain = null;
            }
        } = Solids.DefaultSegments;

        [Display(GroupName = Body, Name = "太さ", Description = "輪の太さで、100% で穴が無くなります")]
        [TextBoxSlider("F0", "%", 5, 100)]
        [ShowPropertyEditorWhen(nameof(IsTorus), true)]
        public int Thickness
        {
            get;
            set
            {
                Set(ref field, Math.Clamp(value, 1, 100));
                plain = null;
            }
        } = Solids.DefaultThickness;

        public ImmutableList<double> MeshPoints
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshPoints), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<int> MeshFaces
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshFaces), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<int> MeshSides
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshSides), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<int> MeshGroups
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshGroups), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<int> MeshSmooth
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshSmooth), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<int> MeshLoose
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshLoose), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<double> MeshMarks
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshMarks), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<double> MeshUvs
        {
            get;
            set { Set(ref field, value ?? [], nameof(MeshUvs), nameof(HasMesh)); Forget(); }
        } = [];

        public ImmutableList<double> MeshKeys
        {
            get;
            set { Set(ref field, value ?? []); Forget(); }
        } = [];

        public ImmutableList<double> MeshWeights
        {
            get;
            set { Set(ref field, value ?? []); Forget(); }
        } = [];

        public bool HasMesh => MeshPoints.Count >= 3;

        private MeshTopology? edited;
        private MeshTopology? shown;
        private Writing? writing;
        private int meshVersion;
        private (MeshTopology Basis, float[] Weights, MeshTopology Mixed)? mixed;
        private MeshTopology? plain;
        private const int WaitsForModifiersMs = 30;

        private readonly EvaluationSlot baked = new();
        private readonly EvaluationSlot trimmed = new();
        private readonly EvaluationSlot bare = new();
        private readonly EvaluationSlot exported = new();

        [Display(GroupName = Free, Name = "作り方", Description = "下書きの形を、そのまま奥行きへ押し出すか、縦の軸のまわりに回すか")]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsCustom), true)]
        public OutlineForm Form { get; set => Set(ref field, value, nameof(Form), nameof(GroupCount), nameof(IsCurved), nameof(IsExtruded)); } = OutlineForm.Extrude;

        [Display(GroupName = Free, Name = "厚み", Description = "押し出す奥行きで、100% で下書きのいちばん長い差し渡しと同じになります")]
        [TextBoxSlider("F0", "%", 1, 300)]
        [ShowPropertyEditorWhen(nameof(IsExtruded), true)]
        public int Depth { get; set => Set(ref field, Math.Clamp(value, 1, 1000)); } = 50;

        [Display(GroupName = Free, Name = "点の数", Description = "下書きの点の数で、増やすといちばん長い辺の真ん中に足され、減らすと終わりの点から消えます")]
        [TextBoxSlider("F0", "個", OutlineSolid.MinPoints, 24)]
        [ShowPropertyEditorWhen(nameof(IsCustom), true)]
        public int PointCount { get; set => Set(ref field, Math.Clamp(value, OutlineSolid.MinPoints, OutlineSolid.MaxPoints)); } = 4;

        [Display(AutoGenerateField = true)]
        [ShowPropertyEditorWhen(nameof(IsCustom), true)]
        public ImmutableList<OutlinePoint> Outline { get; set => Set(ref field, value); } = [];

        public bool IsCustom => Solid == SolidKind.Custom;

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
        public CubeFill Fill { get; set => Set(ref field, value, nameof(Fill), nameof(IsSolidColor), nameof(UsesMaterials), nameof(ShowsGloss)); } = CubeFill.Solid;

        [Display(GroupName = Paint, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit), nameof(ShowsGloss)); }

        [Display(GroupName = Paint, Name = "自動スムーズ", Description = "なめらかに見せる面のうち、決めた角度よりきつく折れた辺だけを硬い角として残します")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsCustom), false)]
        public bool AutoSmooth { get; set => Set(ref field, value, nameof(AutoSmooth), nameof(ShowsSmoothAngle)); }

        [Display(GroupName = Paint, Name = "スムーズの角度", Description = "これより緩く折れた辺はなめらかに、きつく折れた辺は硬く見せます")]
        [TextBoxSlider("F0", "°", 0, 180)]
        [ShowPropertyEditorWhen(nameof(ShowsSmoothAngle), true)]
        public double SmoothAngle { get; set => Set(ref field, Math.Clamp(value, 0, 180)); } = 30;

        public bool ShowsSmoothAngle => AutoSmooth && !IsCustom;

        [Display(GroupName = Modify, Name = "")]
        [ModifierStackEditor]
        [ShowPropertyEditorWhen(nameof(IsCustom), false)]
        public ImmutableList<ShapeModifier> Modifiers { get; set => Set(ref field, value); } = [];

        [Display(GroupName = Keys, Name = "")]
        [ShapeKeysEditor]
        [ShowPropertyEditorWhen(nameof(IsCustom), false)]
        public ImmutableList<ShapeKey> ShapeKeys { get; set => Set(ref field, value); } = [];

        public int ActiveShapeKey
        {
            get;
            set
            {
                SetWithoutUndoRedo(ref field, value);
                shown = null;
            }
        } = -1;

        [Display(GroupName = Rig, Name = "")]
        [ShapeBonesEditor]
        [ShowPropertyEditorWhen(nameof(IsCustom), false)]
        public ImmutableList<ShapeBone> Bones
        {
            get;
            set
            {
                foreach (var bone in field)
                    bone.PropertyChanged -= OnBoneChanged;

                Set(ref field, value, nameof(Bones), nameof(HasBones), nameof(IModelHost.Model));

                foreach (var bone in field)
                    bone.PropertyChanged += OnBoneChanged;
            }
        } = [];

        [Display(GroupName = Rig, Name = "")]
        [BonePoseList]
        [ShowPropertyEditorWhen(nameof(HasBones), true)]
        public ImmutableList<ModelBonePose> BonePoses { get; set => Set(ref field, value); } = [];

        public bool HasBones => Bones.Count > 0;

        private void OnBoneChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(IModelHost.Model));

        ModelData? IModelHost.Model => ShapeArmature.Build(Bones);

        ModelBonePose IBoneHost.BoneFor(string name)
        {
            if (BonePoses.FirstOrDefault(pose => pose.Name == name) is { } found)
                return found;

            var made = new ModelBonePose(name);

            BonePoses = BonePoses.Add(made);

            return made;
        }

        void IBoneHost.DropBoneIfUntouched(string name)
        {
            if (BonePoses.FirstOrDefault(pose => pose.Name == name) is { IsUntouched: true } found)
                BonePoses = BonePoses.Remove(found);
        }

        internal Matrix4x4[]? SkinAt(in FrameContext time) => ShapeArmature.Build(Bones) is { } model ? ShapeArmature.Skin(model, BonePoses, time) : null;

        internal Matrix4x4? BoneShift(string name, in FrameContext time)
        {
            if (ShapeArmature.Build(Bones) is not { } model)
                return null;

            var index = Array.FindIndex(model.Bones, bone => bone.Name == name);

            if (index < 0)
                return null;

            var scale = Matrix4x4.CreateScale(WorldScale.ToWorld(Size.GetFloat(time)));

            return Matrix4x4.Invert(scale, out var back) ? back * ShapeArmature.Skin(model, BonePoses, time)[index] * scale : null;
        }

        internal void AddBone(ShapeBone bone)
        {
            var count = HasMesh ? Basis().Points.Length : 0;

            if (count > 0 && MeshWeights.Count == Bones.Count * count)
                MeshWeights = MeshWeights.AddRange(Enumerable.Repeat(0.0, count));

            Bones = Bones.Add(bone);
        }

        internal void RemoveBone(int index)
        {
            if (index < 0 || index >= Bones.Count)
                return;

            var count = HasMesh ? Basis().Points.Length : 0;

            if (count > 0 && MeshWeights.Count == Bones.Count * count)
                MeshWeights = MeshWeights.RemoveRange(index * count, count);

            Bones = Bones.RemoveAt(index);
        }

        internal void MoveBone(int from, int to)
        {
            if (from < 0 || to < 0 || from >= Bones.Count || to >= Bones.Count || from == to)
                return;

            var count = HasMesh ? Basis().Points.Length : 0;

            if (count > 0 && MeshWeights.Count == Bones.Count * count)
            {
                var flat = MeshWeights.ToBuilder();

                for (var i = 0; i < count; i++)
                {
                    flat[from * count + i] = MeshWeights[to * count + i];
                    flat[to * count + i] = MeshWeights[from * count + i];
                }

                MeshWeights = flat.ToImmutable();
            }

            Bones = Bones.SetItem(from, Bones[to]).SetItem(to, Bones[from]);
        }

        internal void AutoWeights()
        {
            if (Bones.Count == 0)
                return;

            MeshWeights = FlattenWeights(ShapeArmature.AutoWeights(Basis(), Bones));
        }

        internal void SetWeights(IReadOnlyCollection<int> vertices, int bone, float weight)
        {
            if (bone < 0 || bone >= Bones.Count)
                return;

            var basis = Basis();
            var weights = basis.Weights is { } known && known.Length == Bones.Count ? known.Select(one => (float[])one.Clone()).ToArray() : [.. Bones.Select(_ => new float[basis.Points.Length])];

            foreach (var vertex in vertices)
            {
                if (vertex >= 0 && vertex < basis.Points.Length)
                    weights[bone][vertex] = weight;
            }

            MeshWeights = FlattenWeights(weights);
        }

        private static ImmutableList<double> FlattenWeights(float[][] weights) => [.. weights.SelectMany(weight => weight.Select(value => (double)value))];

        internal bool IsEditing { get; set; }

        internal bool UsesEditedGeometry => HasMesh || (!IsCustom && (AutoSmooth || Modifiers.Any(modifier => modifier.IsEnabled)));

        internal Func<int, BooleanTarget?>? Targets { get; set; }

        [Display(GroupName = Paint, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(ShowsGloss), true)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Paint, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(ShowsGloss), true)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        public bool ShowsGloss => !IsUnlit && !UsesMaterials;

        [Display(GroupName = Paint, Name = "色", Description = "立体の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsSolidColor), true)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        [Display(AutoGenerateField = true)]
        public ImmutableList<FaceColor> FaceColors { get; set => Set(ref field, value); } = [];

        public ImmutableList<Color> StoredFaceColors { get; set => Set(ref field, value); } = [];

        public bool IsSolidColor => Fill == CubeFill.Solid;

        [Display(GroupName = Paint, Name = "")]
        [MaterialSlotsEditor]
        [ShowPropertyEditorWhen(nameof(UsesMaterials), true)]
        public ImmutableList<ShapeMaterial> Materials { get; set => Set(ref field, value); } = [];

        public bool UsesMaterials => Fill == CubeFill.Material;

        internal IReadOnlyList<ShapeMaterial> ShownMaterials => UsesMaterials && Materials.Count > 0 ? Materials : [];

        public bool IsCurved => Solids.IsCurved(Solid) || (IsCustom && Form == OutlineForm.Lathe);

        public bool IsTorus => Solid == SolidKind.Torus;

        internal int GroupCount => IsCustom ? OutlineSolid.GroupCountOf(Form) : Solids.GroupCountOf(Solid);

        internal IReadOnlyList<Color> ResolvedColors => Fill == CubeFill.Solid ? [Color] : FaceColors.Count > 0 ? [.. FaceColors.Select(f => f.Color)] : StoredFaceColors;

        public Shape3DParameter() : this(null)
        {
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            SyncOutline();
            SyncFaceColors();
            SyncMaterials();
        }

        internal SurfaceGloss GetGloss(in FrameContext time) => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        internal Matrix4x4 GetLocalMatrix(in FrameContext time) => Matrix4x4.CreateScale(WorldScale.ToWorld(Size.GetFloat(time))) * GetTurn(time);

        internal MeshTopology ShownMesh()
        {
            if (writing is { } busy)
                return busy.Display;

            if (!HasMesh)
                return plain ??= MeshTopology.FromGeometry(Solids.Get(Solid, Segments, Thickness));

            if (shown is { } known)
                return known;

            var version = meshVersion;
            var basis = StoredBasis();
            var display = ActiveKeyOf(basis) is { } key ? basis.WithPoints(Shifted(basis.Points, key, 1f)) : basis;

            if (version == meshVersion)
                shown = display;

            return display;
        }

        private MeshTopology StoredBasis()
        {
            if (writing is { } busy)
                return busy.Basis;

            if (edited is { } known)
                return known;

            var version = meshVersion;
            var read = ReadMesh();

            if (version == meshVersion)
                edited = read;

            return read;
        }

        private void Write(MeshTopology basis, MeshTopology display, Action store)
        {
            writing = new Writing(basis, display);

            try
            {
                store();
            }
            finally
            {
                edited = basis;
                shown = display;
                writing = null;
            }
        }

        private sealed record Writing(MeshTopology Basis, MeshTopology Display);

        private Vector3[]? ActiveKeyOf(MeshTopology mesh)
            => mesh.Keys is { } keys && ActiveShapeKey >= 0 && ActiveShapeKey < keys.Length ? keys[ActiveShapeKey] : null;

        private static Vector3[] Shifted(Vector3[] points, Vector3[] offsets, float amount)
        {
            var moved = (Vector3[])points.Clone();

            for (var i = 0; i < moved.Length; i++)
                moved[i] += offsets[i] * amount;

            return moved;
        }

        private MeshTopology Mixed(in FrameContext time)
        {
            if (IsEditing || !HasMesh)
                return ShownMesh();

            var basis = StoredBasis();

            if (basis.Keys is not { } keys)
                return basis;

            var weights = new float[keys.Length];

            for (var k = 0; k < keys.Length && k < ShapeKeys.Count; k++)
                weights[k] = ShapeKeys[k].Value.GetFloat(time) / 100f;

            if (weights.All(weight => weight == 0f))
                return basis;

            if (mixed is { } known && ReferenceEquals(known.Basis, basis) && known.Weights.SequenceEqual(weights))
                return known.Mixed;

            var points = basis.Points;

            for (var k = 0; k < keys.Length; k++)
            {
                if (weights[k] != 0f)
                    points = Shifted(points, keys[k], weights[k]);
            }

            var made = basis.WithPoints(points);

            mixed = (basis, weights, made);

            return made;
        }

        internal void AddShapeKey()
        {
            var basis = Basis();

            MeshKeys = MeshKeys.AddRange(Enumerable.Repeat(0.0, basis.Points.Length * 3));
            ShapeKeys = ShapeKeys.Add(new ShapeKey { Name = $"キー {ShapeKeys.Count + 1}" });
        }

        internal void RemoveShapeKey(int index)
        {
            if (index < 0 || index >= ShapeKeys.Count)
                return;

            var size = Basis().Points.Length * 3;

            if (MeshKeys.Count >= (index + 1) * size)
                MeshKeys = MeshKeys.RemoveRange(index * size, size);

            ShapeKeys = ShapeKeys.RemoveAt(index);
            ActiveShapeKey = ActiveShapeKey == index ? -1 : ActiveShapeKey > index ? ActiveShapeKey - 1 : ActiveShapeKey;
        }

        internal void MoveShapeKey(int from, int to)
        {
            if (from < 0 || to < 0 || from >= ShapeKeys.Count || to >= ShapeKeys.Count || from == to)
                return;

            var size = Basis().Points.Length * 3;

            if (MeshKeys.Count == ShapeKeys.Count * size)
            {
                var flat = MeshKeys.ToBuilder();
                var first = MeshKeys.GetRange(from * size, size);
                var second = MeshKeys.GetRange(to * size, size);

                for (var i = 0; i < size; i++)
                {
                    flat[from * size + i] = second[i];
                    flat[to * size + i] = first[i];
                }

                MeshKeys = flat.ToImmutable();
            }

            ShapeKeys = ShapeKeys.SetItem(from, ShapeKeys[to]).SetItem(to, ShapeKeys[from]);
            ActiveShapeKey = ActiveShapeKey == from ? to : ActiveShapeKey == to ? from : ActiveShapeKey;
        }

        private MeshTopology Basis()
        {
            EnsureMesh();

            return StoredBasis();
        }

        internal SurfaceGeometry EditedGeometry(in FrameContext time) => Evaluate(null, time, true, baked, !OnScreenThread).Geometry;

        private static bool OnScreenThread => System.Windows.Application.Current?.Dispatcher.CheckAccess() == true;

        internal MeshTopology BareMesh(in FrameContext time) => Evaluate(null, time, false, bare, true).Mesh;

        internal SurfaceGeometry ExportGeometry(in FrameContext time)
        {
            if (!UsesEditedGeometry)
                return Solids.Get(Solid, Segments, Thickness);

            var editing = IsEditing;

            IsEditing = false;

            try
            {
                return Evaluate(null, time, true, exported, true).Geometry;
            }
            finally
            {
                IsEditing = editing;
            }
        }

        internal IReadOnlyList<ShapeSlot> ExportSlots(in FrameContext time)
        {
            if (ShownMaterials is { Count: > 0 } materials)
            {
                var at = time;

                return [.. materials.Select(material => ShapeSlot.Of(material, at))];
            }

            return [.. ResolvedColors.Select((shade, i) => ShapeSlot.Plain($"色 {i + 1}", shade))];
        }

        internal void TakeMaterials(IReadOnlyList<ShapeMaterial> added)
        {
            if (added.Count == 0)
                return;

            if (!UsesMaterials || Materials.Count == 0)
            {
                Materials = [new ShapeMaterial { Name = "材質 1", Color = Color }];
                Fill = CubeFill.Material;
            }

            Materials = Materials.AddRange(added);
        }

        internal bool ApplyModifier(ShapeModifier modifier)
        {
            var index = Modifiers.IndexOf(modifier);

            if (index < 0)
                return false;

            var mesh = EnsureMesh();

            if (modifier.Step(Context(new FrameContext(0, 1, AppliedFps), true)) is { } step)
            {
                var basis = Basis();
                var made = step.Apply(basis.WithKeys(null));

                if (basis.Keys is { } keys)
                {
                    var moved = new Vector3[keys.Length][];

                    for (var k = 0; k < keys.Length; k++)
                    {
                        var shaped = step.Apply(basis.WithKeys(null).WithPoints(Shifted(basis.Points, keys[k], 1f)));

                        if (shaped.Points.Length != made.Points.Length)
                            return false;

                        moved[k] = [.. shaped.Points.Select((point, i) => point - made.Points[i])];
                    }

                    made = made.Carry(basis.WithKeys(null)).WithKeys(moved);
                }
                else
                {
                    made = made.Carry(basis);
                }

                var active = ActiveShapeKey;

                ActiveShapeKey = -1;
                StoreMesh(made);
                ActiveShapeKey = active;
            }

            Modifiers = Modifiers.RemoveAt(index);

            return true;
        }

        private ModifierContext Context(in FrameContext time, bool targets)
        {
            var size = Size.GetFloat(time);

            return new ModifierContext(time, size > 0f ? 1f / size : 0f, targets ? Targets : null, SkinAt(time));
        }

        private Evaluation Evaluate(IReadOnlySet<int>? hidden, in FrameContext time, bool targets, EvaluationSlot slot, bool waits)
        {
            var cage = Mixed(time);
            var angle = SmoothingAngle;
            var context = Context(time, targets);
            ModifierStep[] steps = IsCustom ? [] : [.. Modifiers.Where(modifier => modifier.IsEnabled).Select(modifier => modifier.Step(context)).OfType<ModifierStep>()];

            if (slot.Done is { } known && known.Matches(cage, hidden, angle, steps))
                return known;

            if (waits || steps.All(step => step.IsQuick))
                return slot.Done = Build(cage, hidden, angle, steps);

            Task running;

            lock (slot)
            {
                running = slot.Running ??= Task.Run(() =>
                {
                    try
                    {
                        slot.Done = Build(cage, hidden, angle, steps);
                    }
                    finally
                    {
                        lock (slot)
                            slot.Running = null;

                        SceneRevision.Advance();
                    }
                });
            }

            running.Wait(WaitsForModifiersMs);

            if (slot.Done is { } finished && (finished.Matches(cage, hidden, angle, steps) || finished.Steps.Length > 0))
                return finished;

            if (slot.Plain is { } plain && plain.Matches(cage, hidden, angle, []))
                return plain;

            return slot.Plain = Build(cage, hidden, angle, []);
        }

        private static Evaluation Build(MeshTopology cage, IReadOnlySet<int>? hidden, float? angle, ModifierStep[] steps)
        {
            var mesh = hidden is null ? cage : Trimmed(cage, hidden);

            foreach (var step in steps)
                mesh = step.Apply(mesh);

            return new Evaluation(cage, hidden, angle, steps, mesh, mesh.ToGeometry(angle));
        }

        private sealed class EvaluationSlot
        {
            public Evaluation? Done { get; set; }

            public Evaluation? Plain { get; set; }

            public Task? Running { get; set; }
        }

        private static MeshTopology Trimmed(MeshTopology mesh, IReadOnlySet<int> hidden)
        {
            var kept = Enumerable.Range(0, mesh.Faces.Length).Where(face => !hidden.Contains(face)).ToArray();

            return mesh.With(mesh.Points, kept.Select(face => mesh.Faces[face]), kept.Select(face => mesh.Groups[face]), kept.Select(face => mesh.Smooth[face]))
                .WithUvs(mesh.Uvs is { } uvs ? [.. kept.Select(face => uvs[face])] : null)
                .WithWeights(mesh.Weights);
        }

        private sealed record Evaluation(MeshTopology Cage, IReadOnlySet<int>? Hidden, float? Angle, ModifierStep[] Steps, MeshTopology Mesh, SurfaceGeometry Geometry)
        {
            public bool Matches(MeshTopology cage, IReadOnlySet<int>? hidden, float? angle, ModifierStep[] steps)
                => ReferenceEquals(Cage, cage) && ReferenceEquals(Hidden, hidden) && Angle == angle && Steps.SequenceEqual(steps);
        }

        private float? SmoothingAngle => AutoSmooth && !IsCustom ? float.DegreesToRadians((float)SmoothAngle) : null;

        internal IReadOnlySet<int>? PreviewHidden { get; set; }

        internal SurfaceGeometry? PreviewGeometry(in FrameContext time)
            => PreviewHidden is { Count: > 0 } hidden ? Evaluate(hidden, time, true, trimmed, !OnScreenThread).Geometry : null;

        internal MeshTopology StoreMesh(MeshTopology built)
        {
            if (ShapeKeys.Count > 0 && built.Keys is null && HasMesh)
                built = built.WithKeys(MeshUv.CarriedKeys(built, ShownMesh()));

            if (Bones.Count > 0 && built.Weights is null && HasMesh)
                built = built.WithWeights(MeshUv.CarriedWeights(built, ShownMesh()));

            var display = built;

            if (ActiveKeyOf(built) is { } key)
                built = built.WithPoints(Shifted(built.Points, key, -1f));

            var corners = new List<int>();
            var sides = new List<int>();

            foreach (var face in built.Faces)
            {
                corners.AddRange(face);
                sides.Add(face.Length);
            }

            Write(built, display, () =>
            {
                MeshPoints = Flatten(built.Points);
                MeshFaces = [.. corners];
                MeshSides = [.. sides];
                MeshGroups = [.. built.Groups];
                MeshSmooth = [.. built.Smooth.Select(one => one ? 1 : 0)];
                MeshLoose = [.. built.LooseEdges.SelectMany(edge => new[] { edge.A, edge.B })];
                MeshMarks = [.. built.Marks.SelectMany(pair => new double[] { pair.Key.A, pair.Key.B, (pair.Value.Sharp ? SharpBit : 0) | (pair.Value.Seam ? SeamBit : 0), pair.Value.Crease })];
                MeshUvs = built.Uvs is { } uvs ? [.. uvs.SelectMany(face => face.SelectMany(uv => new double[] { uv.X, uv.Y }))] : [];
                MeshKeys = built.Keys is { } keys && ShapeKeys.Count == keys.Length ? [.. keys.SelectMany(Flatten)] : [];
                MeshWeights = built.Weights is { } weights && Bones.Count == weights.Length ? FlattenWeights(weights) : [];
            });

            return display;
        }

        internal MeshTopology StorePoints(MeshTopology built)
        {
            if (HasMesh && StoredBasis() is { } basis && ActiveKeyOf(basis) is not null && basis.Keys is { } keys && built.Points.Length == basis.Points.Length)
            {
                var changed = (Vector3[][])keys.Clone();

                changed[ActiveShapeKey] = [.. built.Points.Select((point, i) => point - basis.Points[i])];

                var stored = basis.WithKeys(changed);
                var display = stored.WithPoints(built.Points);

                Write(stored, display, () => MeshKeys = [.. changed.SelectMany(Flatten)]);

                return display;
            }

            Write(built, built, () => MeshPoints = Flatten(built.Points));

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
            MeshLoose = [];
            MeshMarks = [];
            MeshUvs = [];
            MeshKeys = [];
            MeshWeights = [];
            ShapeKeys = [];
            ActiveShapeKey = -1;
        }

        internal Vector2[] OutlinePoints(in FrameContext time)
        {
            var points = new Vector2[Outline.Count];

            for (var i = 0; i < points.Length; i++)
                points[i] = new Vector2(Outline[i].X.GetFloat(time), Outline[i].Y.GetFloat(time));

            return points;
        }

        private void Forget()
        {
            meshVersion++;
            edited = null;
            shown = null;
        }

        private static ImmutableList<double> Flatten(Vector3[] points)
        {
            var flat = ImmutableList.CreateBuilder<double>();

            foreach (var point in points)
            {
                flat.Add(point.X);
                flat.Add(point.Y);
                flat.Add(point.Z);
            }

            return flat.ToImmutable();
        }

        private MeshTopology ReadMesh()
        {
            var (storedPoints, storedSides, storedFaces, storedGroups, storedSmooth, storedLoose, storedMarks) = (MeshPoints, MeshSides, MeshFaces, MeshGroups, MeshSmooth, MeshLoose, MeshMarks);
            var points = new Vector3[storedPoints.Count / 3];

            for (var i = 0; i < points.Length; i++)
                points[i] = new Vector3((float)storedPoints[i * 3], (float)storedPoints[i * 3 + 1], (float)storedPoints[i * 3 + 2]);

            var faces = new int[storedSides.Count][];
            var groups = new int[faces.Length];
            var smooth = new bool[faces.Length];
            var at = 0;

            for (var f = 0; f < faces.Length; f++)
            {
                var sides = Math.Clamp(storedSides[f], 3, 256);
                var corners = new int[sides];

                for (var i = 0; i < sides; i++)
                    corners[i] = at + i < storedFaces.Count ? Math.Clamp(storedFaces[at + i], 0, Math.Max(points.Length - 1, 0)) : 0;

                at += sides;
                faces[f] = corners;
                groups[f] = f < storedGroups.Count ? storedGroups[f] : 0;
                smooth[f] = f < storedSmooth.Count && storedSmooth[f] != 0;
            }

            var loose = new List<MeshEdge>(storedLoose.Count / 2);

            for (var i = 0; i + 1 < storedLoose.Count; i += 2)
                loose.Add(new MeshEdge(storedLoose[i], storedLoose[i + 1]));

            var marks = new Dictionary<MeshEdge, EdgeMark>();

            for (var i = 0; i + 3 < storedMarks.Count; i += 4)
            {
                var flags = (int)storedMarks[i + 2];

                marks[MeshTopology.Key((int)storedMarks[i], (int)storedMarks[i + 1])] = new EdgeMark((flags & SharpBit) != 0, (float)storedMarks[i + 3], (flags & SeamBit) != 0);
            }

            return new MeshTopology(points, faces, groups, smooth, GroupCount, loose, marks, ReadUvs(faces), ReadKeys(points.Length), ReadWeights(points.Length));
        }

        private float[][]? ReadWeights(int count)
        {
            var (stored, bones) = (MeshWeights, Bones.Count);

            if (bones == 0 || stored.Count != bones * count)
                return null;

            var weights = new float[bones][];

            for (var b = 0; b < bones; b++)
            {
                weights[b] = new float[count];

                for (var i = 0; i < count; i++)
                    weights[b][i] = (float)stored[b * count + i];
            }

            return weights;
        }

        private Vector3[][]? ReadKeys(int count)
        {
            var (storedKeys, keyCount) = (MeshKeys, ShapeKeys.Count);

            if (keyCount == 0 || storedKeys.Count != keyCount * count * 3)
                return null;

            var keys = new Vector3[keyCount][];

            for (var k = 0; k < keys.Length; k++)
            {
                keys[k] = new Vector3[count];

                for (var i = 0; i < count; i++)
                {
                    var at = (k * count + i) * 3;

                    keys[k][i] = new Vector3((float)storedKeys[at], (float)storedKeys[at + 1], (float)storedKeys[at + 2]);
                }
            }

            return keys;
        }

        private Vector2[][]? ReadUvs(int[][] faces)
        {
            var storedUvs = MeshUvs;

            if (storedUvs.Count != faces.Sum(face => face.Length) * 2)
                return null;

            var uvs = new Vector2[faces.Length][];
            var at = 0;

            for (var f = 0; f < faces.Length; f++)
            {
                uvs[f] = new Vector2[faces[f].Length];

                for (var i = 0; i < uvs[f].Length; i++, at += 2)
                    uvs[f][i] = new Vector2((float)storedUvs[at], (float)storedUvs[at + 1]);
            }

            return uvs;
        }

        private void SyncOutline()
        {
            if (!IsCustom)
                return;

            var wanted = Math.Clamp(PointCount, OutlineSolid.MinPoints, OutlineSolid.MaxPoints);

            if (Outline.Count < OutlineSolid.MinPoints)
            {
                Outline = [.. Ring(wanted).Select(point => new OutlinePoint(point.X, point.Y))];
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

                points.Insert(edge + 1, OutlinePoint.Between(points[edge], points[(edge + 1) % points.Count]));
            }

            Outline = [.. points];
        }

        private static int LongestEdge(List<OutlinePoint> points)
        {
            var found = 0;
            var longest = -1d;

            for (var i = 0; i < points.Count; i++)
            {
                var span = OutlinePoint.DistanceSquared(points[i], points[(i + 1) % points.Count]);

                if (span <= longest)
                    continue;

                longest = span;
                found = i;
            }

            return found;
        }

        private static Vector2[] Ring(int count)
        {
            var points = new Vector2[Math.Max(count, OutlineSolid.MinPoints)];
            var start = MathF.PI / 2f + MathF.PI / points.Length;

            for (var i = 0; i < points.Length; i++)
            {
                var angle = start + MathF.Tau * i / points.Length;

                points[i] = new Vector2(MathF.Round(DefaultRadius * MathF.Cos(angle), 2), MathF.Round(DefaultRadius * MathF.Sin(angle), 2));
            }

            return points;
        }

        private void SyncMaterials()
        {
            if (UsesMaterials && Materials.Count == 0)
                Materials = [new ShapeMaterial { Name = "材質 1", Color = Color }];
        }

        private void SyncFaceColors()
        {
            if (Fill != CubeFill.PerFace)
            {
                if (FaceColors.Count == 0)
                    return;

                StoredFaceColors = [.. FaceColors.Select(f => f.Color)];
                FaceColors = [];
                return;
            }

            var current = FaceColors.Count > 0 ? [.. FaceColors.Select(f => f.Color)] : StoredFaceColors;
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

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices) => new Shape3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Size, RotationX, RotationY, RotationZ, Gloss, GlossSharpness, CameraSyncAnimation, .. PivotAnimatables, .. Outline, .. Modifiers, .. Materials, .. ShapeKeys, .. Bones, .. BonePoses];

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store) => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store) => store.Save(new SharedData(this));

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
}
