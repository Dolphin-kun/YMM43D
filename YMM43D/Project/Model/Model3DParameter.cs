using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Model
{
    internal sealed class Model3DParameter(SharedDataStore? sharedData) : ShapeParameter3DBase(sharedData), IModelLook, ISceneRotation3D, ISceneScale3D, IBoneHost, IMotionHost
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Stretch = "軸ごとの拡大";
        private const string Move = "動き";
        private const string Paint = "色";
        private const string Toon = "トーン";
        private const string Feel = "質感";
        private const string Pattern = "模様";
        private const string Parts = "材質・ボーン・表情";

        private const float FeatherSpan = 400f;
        private const float DefaultFeather = ModelPaint.DefaultFeather * FeatherSpan;

        private readonly ModelSwing springs = new();
        private string syncedFile = string.Empty;

        [Display(GroupName = Body, Name = "ファイル", Description = "obj / gltf / glb / vrm / fbx / pmx のファイル")]
        [FileSelector(FileGroupType.None, CustomFilterName = "3Dモデル", CustomFilterValue = "*.obj;*.gltf;*.glb;*.vrm;*.fbx;*.pmx")]
        public string File
        {
            get;
            set
            {
                Set(ref field, value ?? string.Empty);
                TouchModel();
            }
        } = string.Empty;

        [Display(GroupName = Body, Name = "モデルのまま", Description = "作られたときの大きさのまま出します（1 m が 100 px）")]
        [ToggleSlider]
        public bool IsActualSize { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡し")]
        [AnimationSlider("F1", "px", 0, 1000)]
        [ShowPropertyEditorWhen(nameof(IsActualSize), false)]
        public Animation Size { get; } = new(300, 0, 100000);

        [Display(GroupName = Body, Name = "倍率", Description = "モデルの大きさに掛ける割合")]
        [AnimationSlider("F1", "%", 0, 400)]
        [ShowPropertyEditorWhen(nameof(IsActualSize), true)]
        public Animation Scale { get; } = new(100, 0, 100000);

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Stretch, Name = "X", Description = "横に引き伸ばす割合")]
        [AnimationSlider("F1", "%", 0, 400)]
        public Animation ScaleX { get; } = new(100, 0, 100000);

        [Display(GroupName = Stretch, Name = "Y", Description = "縦に引き伸ばす割合")]
        [AnimationSlider("F1", "%", 0, 400)]
        public Animation ScaleY { get; } = new(100, 0, 100000);

        [Display(GroupName = Stretch, Name = "Z", Description = "奥行きに引き伸ばす割合")]
        [AnimationSlider("F1", "%", 0, 400)]
        public Animation ScaleZ { get; } = new(100, 0, 100000);

        public bool HasMotion => Model is { } model && (model.Animations.Length > 0 || ModelMotions.For(model, MotionFile).Count > 0);

        public bool HasBones => Model is { HasBones: true };

        public bool HasFaces => Model is { HasMorphs: true };

        public bool HasParts => HasBones || HasFaces;

        public bool ShowsSwing => HasBones && Swings;

        [Display(GroupName = Move, Name = "動きのファイル", Description = "モデルとは別のファイルに入っている動きを使います")]
        [FileSelector(FileGroupType.None, CustomFilterName = "動き", CustomFilterValue = "*.fbx;*.gltf;*.glb;*.vrm;*.vrma;*.vmd;*.pmx")]
        [ShowPropertyEditorWhen(nameof(HasBones), true)]
        public string MotionFile { get; set => Set(ref field, value ?? string.Empty, nameof(MotionFile), nameof(HasMotion)); } = string.Empty;

        [Display(GroupName = Move, Name = "動き", Description = "ファイルに入っている動きから選びます")]
        [MotionSelector]
        [ShowPropertyEditorWhen(nameof(HasMotion), true)]
        public string Motion { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        [Display(GroupName = Move, Name = "速さ", Description = "動きを再生する速さ")]
        [AnimationSlider("F1", "%", 0, 400)]
        [ShowPropertyEditorWhen(nameof(HasMotion), true)]
        public Animation MotionSpeed { get; } = new(100, -100000, 100000);

        [Display(GroupName = Move, Name = "開始", Description = "動きの何秒目から始めるか")]
        [AnimationSlider("F2", "秒", 0, 60)]
        [ShowPropertyEditorWhen(nameof(HasMotion), true)]
        public Animation MotionStart { get; } = new(0, -100000, 100000);

        [Display(GroupName = Move, Name = "くり返す", Description = "終わりまで行ったら、はじめに戻ります")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(HasMotion), true)]
        public bool MotionLoops { get; set => Set(ref field, value); } = true;

        [Display(GroupName = Move, Name = "揺らす", Description = "モデルに書かれている揺れもの（髪やスカートなど）を揺らします")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(HasBones), true)]
        public bool Swings
        {
            get;
            set
            {
                Set(ref field, value, nameof(Swings), nameof(ShowsSwing));
                springs.Forget();
            }
        } = true;

        [Display(GroupName = Move, Name = "やわらかさ", Description = "揺れものの揺れやすさ")]
        [AnimationSlider("F0", "%", 0, 300)]
        [ShowPropertyEditorWhen(nameof(ShowsSwing), true)]
        public Animation Softness { get; } = new(100, 0, 100000);

        [Display(GroupName = Move, Name = "揺れの長さ", Description = "揺れが続く長さ")]
        [AnimationSlider("F0", "%", 0, 300)]
        [ShowPropertyEditorWhen(nameof(ShowsSwing), true)]
        public Animation Sway { get; } = new(200, 0, 100000);

        [Display(GroupName = Paint, Name = "色", Description = "モデルの色に掛け合わせる色")]
        [ColorPicker]
        public Color Tint { get => tint; set => Set(ref tint, value); }
        private Color tint = Colors.White;

        [Display(GroupName = Paint, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit), nameof(IsShaded)); }

        public bool IsToon { get; set => Set(ref field, value, nameof(IsToon), nameof(IsShaded), nameof(ToonSteps)); }

        public int ToonLevels { get; set => Set(ref field, Math.Clamp(value, 2, 8), nameof(ToonLevels), nameof(ToonSteps)); } = 2;

        public bool IsShaded => !IsUnlit && ToonSteps >= 2;

        [Display(GroupName = Paint, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Paint, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        [Display(GroupName = Toon, Name = "段数", Description = "明るさを何段に分けて塗るか")]
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
        [AnimationSlider("F0", "%", -50, 50)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation ToonShift { get; } = new(0, -100, 100);

        [Display(GroupName = Toon, Name = "境目のぼかし", Description = "段の境目をどれだけぼかすか")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsShaded), true)]
        public Animation ToonFeather { get; } = new(DefaultFeather, 0, 100);

        [Display(GroupName = Toon, Name = "影の色", Description = "光の当たらないところの色に掛け合わせる色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Color ShadeColor { get => shadeColor; set => Set(ref shadeColor, value); }
        private Color shadeColor = Colors.White;

        [Display(GroupName = Feel, Name = "凹凸", Description = "モデルに入っている法線マップのでこぼこの強さ")]
        [AnimationSlider("F0", "%", 0, 200)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Bump { get; } = new(100, 0, 10000);

        [Display(GroupName = Feel, Name = "発光", Description = "モデルに書かれている光り方の強さで、陰影を切っていても光ります")]
        [AnimationSlider("F0", "%", 0, 300)]
        public Animation Glow { get; } = new(100, 0, 10000);

        [Display(GroupName = Feel, Name = "金属", Description = "金属らしさ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Metallic { get; } = new(0, 0, 100);

        [Display(GroupName = Feel, Name = "つるつる", Description = "表面のなめらかさ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Smoothness { get; } = new(0, 0, 100);

        [Display(GroupName = Feel, Name = "映り込み", Description = "「3D環境」のアイテムで選んだ風景の画像を映します")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Mirror { get; } = new(0, 0, 100);

        [Display(GroupName = Pattern, Name = "繰り返し", Description = "模様をくり返す細かさ")]
        [AnimationSlider("F0", "%", 10, 400)]
        public Animation PatternScale { get; } = new(100, 1, 100000);

        [Display(GroupName = Pattern, Name = "横のずらし", Description = "模様を横にずらす量")]
        [AnimationSlider("F1", "%", -100, 100)]
        public Animation PatternX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Pattern, Name = "縦のずらし", Description = "模様を縦にずらす量")]
        [AnimationSlider("F1", "%", -100, 100)]
        public Animation PatternY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Pattern, Name = "回転", Description = "模様を回します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation PatternSpin { get; } = new(0, -100000, 100000);

        [Display(GroupName = Parts, Name = "")]
        [ModelTabs]
        [ShowPropertyEditorWhen(nameof(HasParts), true)]
        public ModelTab TabRow => Tab;

        public ModelTab Tab { get; set => Set(ref field, value, nameof(Tab), nameof(ShowsMaterials), nameof(ShowsBones), nameof(ShowsFaces)); } = ModelTab.Materials;

        private ModelTab ShownTab => Tab switch
        {
            ModelTab.Bones when !HasBones => ModelTab.Materials,
            ModelTab.Faces when !HasFaces => ModelTab.Materials,
            _ => Tab,
        };

        public bool ShowsMaterials => ShownTab == ModelTab.Materials;

        public bool ShowsBones => ShownTab == ModelTab.Bones;

        public bool ShowsFaces => ShownTab == ModelTab.Faces;

        [Display(GroupName = Parts, Name = "")]
        [MaterialList]
        [ShowPropertyEditorWhen(nameof(ShowsMaterials), true)]
        public ImmutableList<ModelMaterialSlot> Materials { get; set => Set(ref field, value); } = [];

        [Display(GroupName = Parts, Name = "")]
        [ExpressionList]
        [ShowPropertyEditorWhen(nameof(ShowsFaces), true)]
        public ImmutableList<ModelExpressionValue> Expressions { get; set => Set(ref field, value); } = [];

        [Display(GroupName = Parts, Name = "")]
        [BonePoseList]
        [ShowPropertyEditorWhen(nameof(ShowsBones), true)]
        public ImmutableList<ModelBonePose> Bones { get; set => Set(ref field, value); } = [];

        public Model3DParameter() : this(null)
        {
        }

        ModelData? IModelHost.Model => Model;

        ModelData? IModelLook.Model => Model;

        internal ModelData? Model => ModelLibrary.Find(File);

        public Vector4 Tone => Tint.ToVector4();

        private void TouchModel()
        {
            OnPropertyChanged(nameof(HasMotion));
            OnPropertyChanged(nameof(HasBones));
            OnPropertyChanged(nameof(HasFaces));
            OnPropertyChanged(nameof(HasParts));
            OnPropertyChanged(nameof(ShowsSwing));
            OnPropertyChanged(nameof(ShowsMaterials));
            OnPropertyChanged(nameof(ShowsBones));
            OnPropertyChanged(nameof(ShowsFaces));
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            SyncMaterials();
            SyncExpressions();
            TouchModel();
        }

        private void SyncExpressions()
        {
            if (Model is not { } model)
                return;

            if (Expressions.Select(one => one.Name).SequenceEqual(model.Expressions.Select(one => one.Name)))
                return;

            var remaining = Expressions.ToList();

            Expressions =
            [
                .. model.Expressions.Select(one =>
                {
                    var kept = remaining.FirstOrDefault(value => value.Name == one.Name);

                    if (kept is null)
                        return new ModelExpressionValue(one.Name);

                    remaining.Remove(kept);

                    return kept;
                }),
            ];
        }

        private void SyncMaterials()
        {
            if (Model is not { } model)
                return;

            if (syncedFile != File)
            {
                syncedFile = File;

                if (model.PrefersUnlit != IsUnlit)
                    IsUnlit = model.PrefersUnlit;
            }

            if (Materials.Select(slot => slot.Name).SequenceEqual(model.Materials))
                return;

            var remaining = Materials.ToList();

            Materials =
            [
                .. model.Materials.Select(name =>
                {
                    var kept = remaining.FirstOrDefault(slot => slot.Name == name);

                    if (kept is not null)
                        remaining.Remove(kept);

                    return new ModelMaterialSlot(name, kept?.Texture ?? string.Empty);
                }),
            ];
        }

        public IReadOnlyList<float>? GetMorphWeights(ModelData model, in FrameContext time)
        {
            if (!model.HasMorphs)
                return null;

            float[]? played = null;

            if (FindMotion(model) is { } motion)
            {
                played = new float[model.Morphs.Length];
                ModelAnimationSampler.Sample(motion, SecondsOf(motion, time), [], played);
            }

            if (Expressions.Count == 0)
                return played;

            var at = time;
            var weights = Expressions
                .Where(value => value.Name.Length > 0 && !value.IsStill(at))
                .Select(value => (value.Name, value.Weight.GetFloat(at) / 100f));
            var chosen = ModelExpressionWeights.Of(model, weights);

            if (played is null || chosen is null)
                return chosen ?? played;

            for (var i = 0; i < played.Length && i < chosen.Length; i++)
                played[i] = Math.Clamp(played[i] + chosen[i], 0f, 1f);

            return played;
        }

        private ModelAnimation? FindMotion(ModelData model) => ModelMotion.Find(model, MotionFile, Motion);

        private float SecondsOf(ModelAnimation motion, in FrameContext time) => ModelMotion.SecondsOf(motion, time, MotionSpeed.GetFloat(time), MotionStart.GetFloat(time), MotionLoops);

        ModelBonePose IBoneHost.BoneFor(string name) => BoneFor(name);

        void IBoneHost.DropBoneIfUntouched(string name) => DropBoneIfUntouched(name);

        internal ModelBonePose? FindBonePose(string name)
        {
            foreach (var bone in Bones)
            {
                if (bone.Name == name)
                    return bone;
            }

            return null;
        }

        internal ModelBonePose BoneFor(string name)
        {
            if (FindBonePose(name) is { } found)
                return found;

            var made = new ModelBonePose(name);

            Bones = Bones.Add(made);

            return made;
        }

        internal void DropBoneIfUntouched(string name)
        {
            if (FindBonePose(name) is { IsUntouched: true } found)
                Bones = Bones.Remove(found);
        }

        internal ModelPose.PoseFrames GetBoneFrames(ModelData model, in FrameContext time) => ModelPose.Compute(model.Bones, GetPose(model, time));

        public IReadOnlyList<Matrix4x4>? GetPose(ModelData model, in FrameContext time) => GetPose(model, time, null);

        public IReadOnlyList<Matrix4x4>? GetPose(ModelData model, in FrameContext time, in Matrix4x4 world, bool advance) => GetPose(model, time, advance ? world : null);

        private Matrix4x4[]? GetPose(ModelData model, in FrameContext time, Matrix4x4? world)
        {
            if (!model.HasBones)
                return null;

            Matrix4x4[]? pose = null;

            if (FindMotion(model) is { } motion)
            {
                pose = Identity(model.Bones.Length);
                ModelAnimationSampler.Sample(motion, SecondsOf(motion, time), pose, null);
            }

            var posed = ModelMmd.Settle(model, BonePoses.Apply(model, Bones, time, pose));

            return Swing(model, posed, time, world);
        }

        private Matrix4x4[]? Swing(ModelData model, Matrix4x4[]? pose, in FrameContext time, Matrix4x4? world)
        {
            if (!Swings)
                return pose;

            return springs.Apply(model, Bones, pose, time, world, Softness.GetFloat(time) / 100f, Sway.GetFloat(time) / 100f);
        }

        private static Matrix4x4[] Identity(int count)
        {
            var pose = new Matrix4x4[count];

            for (var i = 0; i < count; i++)
                pose[i] = Matrix4x4.Identity;

            return pose;
        }

        public IReadOnlyList<ModelImage?> GetReplacements(ModelData model)
        {
            var replacements = new ModelImage?[model.Materials.Length];

            for (var i = 0; i < replacements.Length; i++)
            {
                if (FindSlot(model, i) is { Texture.Length: > 0 } slot)
                    replacements[i] = ModelLibrary.FindImage(slot.Texture);
            }

            return replacements;
        }

        public IReadOnlyList<(ModelBlend Blend, float Cutoff)?> GetBlends(ModelData model)
        {
            var chosen = new (ModelBlend Blend, float Cutoff)?[model.Materials.Length];

            for (var i = 0; i < chosen.Length; i++)
                chosen[i] = FindSlot(model, i)?.Override;

            return chosen;
        }

        private ModelMaterialSlot? FindSlot(ModelData model, int material)
        {
            var slots = Materials;
            var name = model.Materials[material];

            if (material < slots.Count && slots[material].Name == name)
                return slots[material];

            return slots.FirstOrDefault(candidate => candidate.Name == name);
        }

        public ModelPaint GetPaint(in FrameContext time)
        {
            var paint = ModelPaint.Default with
            {
                Glow = Glow.GetFloat(time) / 100f,
                UvScale = new Vector2(MathF.Max(PatternScale.GetFloat(time) / 100f, 1e-4f)),
                UvOffset = new Vector2(PatternX.GetFloat(time) / 100f, PatternY.GetFloat(time) / 100f),
                UvRotation = float.DegreesToRadians(PatternSpin.GetFloat(time)),
            };

            if (IsUnlit)
                return paint;

            return paint with
            {
                ToonSteps = ToonSteps,
                ToonShift = ToonShift.GetFloat(time) / 100f,
                ToonFeather = ToonFeather.GetFloat(time) / FeatherSpan,
                Shade = ShadeColor.ToVector3(),
                Bump = Bump.GetFloat(time) / 100f,
                Metallic = Metallic.GetFloat(time) / 100f,
                Smoothness = Smoothness.GetFloat(time) / 100f,
                Mirror = Mirror.GetFloat(time) / 100f,
            };
        }

        public SurfaceGloss GetGloss(in FrameContext time) => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        public Matrix4x4 GetLocalMatrix(ModelData model, in FrameContext time)
        {
            var bounds = model.Bounds;
            var extent = bounds.Max - bounds.Min;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
            var fitted = longest > 1e-9f ? WorldScale.ToWorld(Size.GetFloat(time)) / longest : 1f;
            var scale = IsActualSize ? Scale.GetFloat(time) / 100f : fitted;
            var stretch = Stretch3D.Matrix(scale, ScaleX.GetFloat(time), ScaleY.GetFloat(time), ScaleZ.GetFloat(time));

            return Matrix4x4.CreateTranslation(-bounds.Center) * stretch * GetTurn(time);
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices) => new Model3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Size, Scale, RotationX, RotationY, RotationZ, ScaleX, ScaleY, ScaleZ, MotionSpeed, MotionStart, Softness, Sway, Gloss, GlossSharpness, ToonShift, ToonFeather, Bump, Glow, Metallic, Smoothness, Mirror, PatternScale, PatternX, PatternY, PatternSpin, CameraSyncAnimation, .. PivotAnimatables, .. Bones, .. Expressions];

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store) => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store) => store.Save(new SharedData(this));

        private sealed class SharedData
        {
            public string File { get; set; } = string.Empty;
            public bool IsActualSize { get; set; } = true;
            public string Motion { get; set; } = string.Empty;
            public Animation MotionSpeed { get; } = new(100, -100000, 100000);
            public Animation MotionStart { get; } = new(0, -100000, 100000);
            public bool MotionLoops { get; set; } = true;
            public bool Swings { get; set; } = true;
            public Animation Softness { get; } = new(100, 0, 100000);
            public Animation Sway { get; } = new(200, 0, 100000);
            public Animation Size { get; } = new(300, 0, 100000);
            public Animation Scale { get; } = new(100, 0, 100000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public Animation ScaleX { get; } = new(100, 0, 100000);
            public Animation ScaleY { get; } = new(100, 0, 100000);
            public Animation ScaleZ { get; } = new(100, 0, 100000);
            public Animation Gloss { get; } = new(0, 0, 1000);
            public Animation GlossSharpness { get; } = new(50, 0, 100);
            public Color Tint { get; set; } = Colors.White;
            public bool IsUnlit { get; set; }
            public bool IsToon { get; set; }
            public int ToonLevels { get; set; } = 2;
            public Animation ToonShift { get; } = new(0, -100, 100);
            public Animation ToonFeather { get; } = new(DefaultFeather, 0, 100);
            public Color ShadeColor { get; set; } = Colors.White;
            public Animation Bump { get; } = new(100, 0, 10000);
            public Animation Glow { get; } = new(100, 0, 10000);
            public Animation Metallic { get; } = new(0, 0, 100);
            public Animation Smoothness { get; } = new(0, 0, 100);
            public Animation Mirror { get; } = new(0, 0, 100);
            public Animation PatternScale { get; } = new(100, 1, 100000);
            public Animation PatternX { get; } = new(0, -100000, 100000);
            public Animation PatternY { get; } = new(0, -100000, 100000);
            public Animation PatternSpin { get; } = new(0, -100000, 100000);
            public ImmutableList<string> MaterialNames { get; set; } = [];
            public ImmutableList<string> MaterialTextures { get; set; } = [];
            public ImmutableList<ModelSlotBlend> MaterialBlends { get; set; } = [];
            public ImmutableList<double> MaterialCutoffs { get; set; } = [];

            public SharedData(Model3DParameter parameter)
            {
                File = parameter.File;
                IsActualSize = parameter.IsActualSize;
                Motion = parameter.Motion;
                MotionSpeed.CopyFrom(parameter.MotionSpeed);
                MotionStart.CopyFrom(parameter.MotionStart);
                MotionLoops = parameter.MotionLoops;
                Swings = parameter.Swings;
                Softness.CopyFrom(parameter.Softness);
                Sway.CopyFrom(parameter.Sway);
                Size.CopyFrom(parameter.Size);
                Scale.CopyFrom(parameter.Scale);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                ScaleX.CopyFrom(parameter.ScaleX);
                ScaleY.CopyFrom(parameter.ScaleY);
                ScaleZ.CopyFrom(parameter.ScaleZ);
                Gloss.CopyFrom(parameter.Gloss);
                GlossSharpness.CopyFrom(parameter.GlossSharpness);
                Tint = parameter.Tint;
                IsUnlit = parameter.IsUnlit;
                IsToon = parameter.IsToon;
                ToonLevels = parameter.ToonLevels;
                ToonShift.CopyFrom(parameter.ToonShift);
                ToonFeather.CopyFrom(parameter.ToonFeather);
                ShadeColor = parameter.ShadeColor;
                Bump.CopyFrom(parameter.Bump);
                Glow.CopyFrom(parameter.Glow);
                Metallic.CopyFrom(parameter.Metallic);
                Smoothness.CopyFrom(parameter.Smoothness);
                Mirror.CopyFrom(parameter.Mirror);
                PatternScale.CopyFrom(parameter.PatternScale);
                PatternX.CopyFrom(parameter.PatternX);
                PatternY.CopyFrom(parameter.PatternY);
                PatternSpin.CopyFrom(parameter.PatternSpin);
                MaterialNames = [.. parameter.Materials.Select(slot => slot.Name)];
                MaterialTextures = [.. parameter.Materials.Select(slot => slot.Texture)];
                MaterialBlends = [.. parameter.Materials.Select(slot => slot.Blend)];
                MaterialCutoffs = [.. parameter.Materials.Select(slot => slot.Cutoff)];
            }

            public void CopyTo(Model3DParameter parameter)
            {
                parameter.File = File;
                parameter.IsActualSize = IsActualSize;
                parameter.Motion = Motion;
                parameter.MotionSpeed.CopyFrom(MotionSpeed);
                parameter.MotionStart.CopyFrom(MotionStart);
                parameter.MotionLoops = MotionLoops;
                parameter.Swings = Swings;
                parameter.Softness.CopyFrom(Softness);
                parameter.Sway.CopyFrom(Sway);
                parameter.Size.CopyFrom(Size);
                parameter.Scale.CopyFrom(Scale);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.ScaleX.CopyFrom(ScaleX);
                parameter.ScaleY.CopyFrom(ScaleY);
                parameter.ScaleZ.CopyFrom(ScaleZ);
                parameter.Gloss.CopyFrom(Gloss);
                parameter.GlossSharpness.CopyFrom(GlossSharpness);
                parameter.Tint = Tint;
                parameter.IsUnlit = IsUnlit;
                parameter.IsToon = IsToon;
                parameter.ToonLevels = ToonLevels;
                parameter.ToonShift.CopyFrom(ToonShift);
                parameter.ToonFeather.CopyFrom(ToonFeather);
                parameter.ShadeColor = ShadeColor;
                parameter.Bump.CopyFrom(Bump);
                parameter.Glow.CopyFrom(Glow);
                parameter.Metallic.CopyFrom(Metallic);
                parameter.Smoothness.CopyFrom(Smoothness);
                parameter.Mirror.CopyFrom(Mirror);
                parameter.PatternScale.CopyFrom(PatternScale);
                parameter.PatternX.CopyFrom(PatternX);
                parameter.PatternY.CopyFrom(PatternY);
                parameter.PatternSpin.CopyFrom(PatternSpin);
                parameter.Materials = [.. MaterialNames.Select(Slot)];
            }

            private ModelMaterialSlot Slot(string name, int index)
            {
                var texture = index < MaterialTextures.Count ? MaterialTextures[index] : string.Empty;
                var blend = index < MaterialBlends.Count ? MaterialBlends[index] : ModelSlotBlend.Model;
                var cutoff = index < MaterialCutoffs.Count ? MaterialCutoffs[index] : 0.5;

                return new ModelMaterialSlot(name, texture) { Blend = blend, Cutoff = cutoff };
            }
        }
    }
}
