using System.Collections.Immutable;
using System.ComponentModel;
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
    internal sealed class Model3DParameter : ShapeParameter3DBase
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Paint = "色";
        private const string Bone = "骨";
        private const string Face = "表情";

        [Display(GroupName = Body, Name = "ファイル", Description = "obj / gltf / glb / vrm / fbx のファイル")]
        [FileSelector(FileGroupType.None, CustomFilterName = "3Dモデル", CustomFilterValue = "*.obj;*.gltf;*.glb;*.vrm;*.fbx")]
        public string File { get => file; set => Set(ref file, value ?? string.Empty); }
        private string file = string.Empty;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡し")]
        [AnimationSlider("F1", "px", 0, 1000)]
        public Animation Size { get; } = new(300, 0, 100000);

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Paint, Name = "色", Description = "モデルの色に掛け合わせます。白ならそのまま")]
        [ColorPicker]
        public Color Tint { get => tint; set => Set(ref tint, value); }
        private Color tint = Colors.White;

        [Display(GroupName = Paint, Name = "陰影をつけない",
            Description = "光源を無視して、モデルの色のまま塗ります")]
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

        [Display(GroupName = Paint, Name = "アニメ塗り",
            Description = "陰影をなめらかにせず、決まった段数に分けて塗ります。トゥーン調のモデル向け")]
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
        private bool isToon;

        [Display(GroupName = Paint, Name = "塗りの段数", Description = "明るさを何段に分けるか")]
        [TextBoxSlider("F0", "段", 2, 5)]
        [ShowPropertyEditorWhen(nameof(IsShaded), true)]
        public int ToonLevels
        {
            get => toonLevels;
            set => Set(ref toonLevels, Math.Clamp(value, 2, 8));
        }
        private int toonLevels = 2;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsShaded => !IsUnlit && IsToon;

        [Display(GroupName = Paint, Name = "つや",
            Description = "光が映り込んだ明るい点の強さ。0 でつやなし")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Paint, Name = "つやの鋭さ",
            Description = "大きいほど映り込みが小さく締まり、磨いたように見えます")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        [Display(AutoGenerateField = true)]
        public ImmutableList<ModelMaterialSlot> Materials { get => materials; set => Set(ref materials, value); }
        private ImmutableList<ModelMaterialSlot> materials = [];

        // モデルに入っている表情。読み込んだときに並べ、強さだけを動かす。
        [Display(GroupName = Face, Name = "")]
        [ExpressionList]
        public ImmutableList<ModelExpressionValue> Expressions { get => expressions; set => Set(ref expressions, value); }
        private ImmutableList<ModelExpressionValue> expressions = [];

        // 動かした骨だけを持つ。モデルの骨は数が多いので、全部は並べない。
        [Display(GroupName = Bone, Name = "")]
        [BonePoseList]
        public ImmutableList<ModelBonePose> Bones { get => bones; set => Set(ref bones, value); }
        private ImmutableList<ModelBonePose> bones = [];

        public Model3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
            SubscribeChildUndoRedoable(Materials);
            SubscribeChildUndoRedoable(Bones);
            SubscribeChildUndoRedoable(Expressions);
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            SyncMaterials();
            SyncExpressions();
        }

        // モデルに入っている表情を並べ直す。強さは、同じ名前のものを引き継ぐ。
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

                    if (kept is not null)
                    {
                        remaining.Remove(kept);

                        return kept;
                    }

                    return new ModelExpressionValue(one.Name);
                }),
            ];

            SubscribeChildUndoRedoable(Expressions);
        }

        // 表情の強さから、形ごとにどれだけ動かすかを出す。
        internal IReadOnlyList<float>? GetMorphWeights(ModelData model, in FrameContext time)
        {
            if (!model.HasMorphs || Expressions.Count == 0)
                return null;

            float[]? weights = null;

            foreach (var value in Expressions)
            {
                if (value.Name.Length == 0 || value.IsStill(time))
                    continue;

                var strength = value.Weight.GetFloat(time) / 100f;

                foreach (var expression in model.Expressions)
                {
                    if (expression.Name != value.Name)
                        continue;

                    foreach (var bind in expression.Binds)
                    {
                        if (bind.Morph < 0 || bind.Morph >= model.Morphs.Length)
                            continue;

                        weights ??= new float[model.Morphs.Length];
                        weights[bind.Morph] = Math.Clamp(weights[bind.Morph] + (strength * bind.Weight), 0f, 1f);
                    }
                }
            }

            return weights;
        }

        private void SyncMaterials()
        {
            if (Model is not { } model)
                return;

            if (Materials.Select(slot => slot.Name).SequenceEqual(model.Materials))
                return;

            var remaining = Materials.ToList();

            Materials = [.. model.Materials.Select(name =>
            {
                var kept = remaining.FirstOrDefault(slot => slot.Name == name);

                if (kept is not null)
                    remaining.Remove(kept);

                return new ModelMaterialSlot(name, kept?.Texture ?? string.Empty);
            })];
        }

        internal ModelBonePose? FindBonePose(string name)
        {
            foreach (var bone in Bones)
            {
                if (bone.Name == name)
                    return bone;
            }

            return null;
        }

        // 名前の付いた骨の姿勢。無ければ作って足す（3Dプレビューで骨を選んだとき）。
        internal ModelBonePose BoneFor(string name)
        {
            if (FindBonePose(name) is { } found)
                return found;

            var made = new ModelBonePose(name);

            Bones = Bones.Add(made);
            SubscribeChildUndoRedoable(Bones);

            return made;
        }

        // 選んだだけで増えた骨を片付ける。回転を動かしていなければ外す。
        internal void DropBoneIfUntouched(string name)
        {
            if (FindBonePose(name) is { IsUntouched: true } found)
                Bones = Bones.Remove(found);
        }

        // 骨の置き場所。回転を掛ける前（Frames）と掛けたあと（Globals）の両方を返す。
        internal ModelPose.PoseFrames GetBoneFrames(ModelData model, in FrameContext time)
            => ModelPose.Compute(model.Bones, GetPose(model, time));

        // 骨ごとの回転。動かしていない骨は単位行列のまま。
        internal IReadOnlyList<Matrix4x4>? GetPose(ModelData model, in FrameContext time)
        {
            if (!model.HasBones || Bones.Count == 0)
                return null;

            Matrix4x4[]? pose = null;

            foreach (var bone in Bones)
            {
                if (bone.Name.Length == 0 || bone.IsStill(time))
                    continue;

                var rotation = bone.GetRotation(time);

                // 同じ名前の骨が複数あることがある（メッシュごとに骨組みを持つ fbx など）。
                for (var i = 0; i < model.Bones.Length; i++)
                {
                    if (model.Bones[i].Name != bone.Name)
                        continue;

                    pose ??= Identity(model.Bones.Length);
                    pose[i] = rotation;
                }
            }

            return pose;
        }

        private static Matrix4x4[] Identity(int count)
        {
            var pose = new Matrix4x4[count];

            for (var i = 0; i < count; i++)
                pose[i] = Matrix4x4.Identity;

            return pose;
        }

        internal IReadOnlyList<ModelImage?> GetReplacements(ModelData model)
        {
            var replacements = new ModelImage?[model.Materials.Length];

            for (var i = 0; i < replacements.Length; i++)
            {
                if (FindSlot(model, i) is { Texture.Length: > 0 } slot)
                    replacements[i] = ModelLibrary.FindImage(slot.Texture);
            }

            return replacements;
        }

        // 材質ごとの透け方の上書き。「モデルのまま」の材質は null。
        internal IReadOnlyList<(ModelBlend Blend, float Cutoff)?> GetBlends(ModelData model)
        {
            var chosen = new (ModelBlend Blend, float Cutoff)?[model.Materials.Length];

            for (var i = 0; i < chosen.Length; i++)
                chosen[i] = FindSlot(model, i)?.Override;

            return chosen;
        }

        private ModelMaterialSlot? FindSlot(ModelData model, int material)
        {
            var slots = Materials;

            return material < slots.Count && slots[material].Name == model.Materials[material]
                ? slots[material]
                : slots.FirstOrDefault(candidate => candidate.Name == model.Materials[material]);
        }

        public Model3DParameter() : this(null)
        {
        }

        internal ModelData? Model => ModelLibrary.Find(File);

        // 陰影を何段に分けるか。0 ならこれまでどおりなめらかに塗る。
        internal float ToonSteps => !IsUnlit && IsToon ? ToonLevels : 0f;

        internal SurfaceGloss GetGloss(in FrameContext time)
            => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        internal Matrix4x4 GetLocalMatrix(ModelData model, in FrameContext time)
        {
            var bounds = model.Bounds;
            var extent = bounds.Max - bounds.Min;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));

            var scale = longest > 1e-9f ? WorldScale.ToWorld(Size.GetFloat(time)) / longest : 1f;

            return Matrix4x4.CreateTranslation(-bounds.Center)
                 * Matrix4x4.CreateScale(scale)
                 * Rotation3D.ForObject(RotationX.GetFloat(time), RotationY.GetFloat(time), RotationZ.GetFloat(time));
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices)
            => new Model3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Size, RotationX, RotationY, RotationZ, Gloss, GlossSharpness, CameraSyncAnimation, .. Bones, .. Expressions];

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
            public string File { get; set; } = string.Empty;
            public Animation Size { get; } = new(300, 0, 100000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public Animation Gloss { get; } = new(0, 0, 1000);
            public Animation GlossSharpness { get; } = new(50, 0, 100);
            public Color Tint { get; set; } = Colors.White;
            public bool IsUnlit { get; set; }
            public bool IsToon { get; set; }
            public int ToonLevels { get; set; } = 2;
            public ImmutableList<string> MaterialNames { get; set; } = [];
            public ImmutableList<string> MaterialTextures { get; set; } = [];
            public ImmutableList<ModelSlotBlend> MaterialBlends { get; set; } = [];
            public ImmutableList<double> MaterialCutoffs { get; set; } = [];

            public SharedData(Model3DParameter parameter)
            {
                File = parameter.File;
                Size.CopyFrom(parameter.Size);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Gloss.CopyFrom(parameter.Gloss);
                GlossSharpness.CopyFrom(parameter.GlossSharpness);
                Tint = parameter.Tint;
                IsUnlit = parameter.IsUnlit;
                IsToon = parameter.IsToon;
                ToonLevels = parameter.ToonLevels;
                MaterialNames = [.. parameter.Materials.Select(slot => slot.Name)];
                MaterialTextures = [.. parameter.Materials.Select(slot => slot.Texture)];
                MaterialBlends = [.. parameter.Materials.Select(slot => slot.Blend)];
                MaterialCutoffs = [.. parameter.Materials.Select(slot => slot.Cutoff)];
            }

            public void CopyTo(Model3DParameter parameter)
            {
                parameter.File = File;
                parameter.Size.CopyFrom(Size);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.Gloss.CopyFrom(Gloss);
                parameter.GlossSharpness.CopyFrom(GlossSharpness);
                parameter.Tint = Tint;
                parameter.IsUnlit = IsUnlit;
                parameter.IsToon = IsToon;
                parameter.ToonLevels = ToonLevels;
                parameter.Materials = [.. MaterialNames.Select((name, i) =>
                    new ModelMaterialSlot(name, i < MaterialTextures.Count ? MaterialTextures[i] : string.Empty)
                    {
                        Blend = i < MaterialBlends.Count ? MaterialBlends[i] : ModelSlotBlend.Model,
                        Cutoff = i < MaterialCutoffs.Count ? MaterialCutoffs[i] : 0.5,
                    })];
            }
        }
    }
}
