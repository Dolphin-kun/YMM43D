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
    internal sealed class Model3DParameter : ShapeParameter3DBase
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Paint = "色";
        private const string Bone = "骨";

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
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

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

        // 動かした骨だけを持つ。モデルの骨は数が多いので、全部は並べない。
        [Display(GroupName = Bone, Name = "")]
        [BonePoseList]
        public ImmutableList<ModelBonePose> Bones { get => bones; set => Set(ref bones, value); }
        private ImmutableList<ModelBonePose> bones = [];

        public Model3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
            SubscribeChildUndoRedoable(Materials);
            SubscribeChildUndoRedoable(Bones);
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            SyncMaterials();
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
            var slots = Materials;

            for (var i = 0; i < replacements.Length; i++)
            {
                var slot = i < slots.Count && slots[i].Name == model.Materials[i]
                    ? slots[i]
                    : slots.FirstOrDefault(candidate => candidate.Name == model.Materials[i]);

                if (slot is { Texture.Length: > 0 })
                    replacements[i] = ModelLibrary.FindImage(slot.Texture);
            }

            return replacements;
        }

        public Model3DParameter() : this(null)
        {
        }

        internal ModelData? Model => ModelLibrary.Find(File);

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
            => [Size, RotationX, RotationY, RotationZ, Gloss, GlossSharpness, CameraSyncAnimation, .. Bones];

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
            public ImmutableList<string> MaterialNames { get; set; } = [];
            public ImmutableList<string> MaterialTextures { get; set; } = [];

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
                MaterialNames = [.. parameter.Materials.Select(slot => slot.Name)];
                MaterialTextures = [.. parameter.Materials.Select(slot => slot.Texture)];
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
                parameter.Materials = [.. MaterialNames.Select((name, i) =>
                    new ModelMaterialSlot(name, i < MaterialTextures.Count ? MaterialTextures[i] : string.Empty))];
            }
        }
    }
}
