using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Tachie
{
    public class Model3DItemParameter : TachieItemParameterBase, IBoneHost, IMotionHost
    {
        private const string Move = "動き";
        private const string Rotation = "向き";
        private const string Parts = "ボーン";

        [Display(GroupName = Move, Name = "動きのファイル", Description = "モデルとは別のファイルに入っている動きを使います")]
        [FileSelector(FileGroupType.None, CustomFilterName = "動き", CustomFilterValue = "*.fbx;*.gltf;*.glb;*.vrm;*.vrma;*.vmd;*.pmx")]
        [ShowPropertyEditorWhen(nameof(HasBones), true)]
        public string MotionFile { get; set => Set(ref field, value ?? string.Empty, nameof(MotionFile), nameof(HasMotion)); } = string.Empty;

        [Display(GroupName = Move, Name = "動き", Description = "ファイルに入っている動きから選びます")]
        [MotionSelector]
        [ShowPropertyEditorWhen(nameof(HasMotion), true)]
        public string Motion { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        [Display(GroupName = Move, Name = "速さ", Description = "100% でファイルのとおり")]
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

        [Display(GroupName = Rotation, Name = "X", Description = "キャラクター設定の向きに足します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y", Description = "キャラクター設定の向きに足します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z", Description = "キャラクター設定の向きに足します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Parts, Name = "")]
        [BonePoseList]
        [ShowPropertyEditorWhen(nameof(HasBones), true)]
        public ImmutableList<ModelBonePose> Bones { get; set => Set(ref field, value); } = [];

        internal string ModelFile
        {
            get;
            set
            {
                if (field == value)
                    return;

                field = value ?? string.Empty;
                OnPropertyChanged(nameof(ModelFile));
                OnPropertyChanged(nameof(HasMotion));
                OnPropertyChanged(nameof(HasBones));
            }
        } = string.Empty;

        internal ModelData? Model => ModelLibrary.Find(ModelFile);

        ModelData? IModelHost.Model => Model;

        public bool HasMotion => Model is { } model && (model.Animations.Length > 0 || ModelMotions.For(model, MotionFile).Count > 0);

        public bool HasBones => Model is { HasBones: true };

        internal Matrix4x4[]? Sample(ModelData model, in FrameContext time)
            => ModelMotion.Sample(model, ModelMotion.Find(model, MotionFile, Motion), time, MotionSpeed.GetFloat(time), MotionStart.GetFloat(time), MotionLoops);

        public ModelBonePose BoneFor(string name)
        {
            foreach (var bone in Bones)
            {
                if (bone.Name == name)
                    return bone;
            }

            var made = new ModelBonePose(name);

            Bones = Bones.Add(made);

            return made;
        }

        public void DropBoneIfUntouched(string name)
        {
            foreach (var bone in Bones)
            {
                if (bone.Name == name && bone.IsUntouched)
                {
                    Bones = Bones.Remove(bone);
                    return;
                }
            }
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [RotationX, RotationY, RotationZ, MotionSpeed, MotionStart, .. Bones];
    }
}
