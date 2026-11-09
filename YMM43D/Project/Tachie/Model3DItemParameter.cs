using System.Numerics;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    public class Model3DItemParameter : TachieItemParameterBase, IBoneHost
    {
        private const string Move = "動き";
        private const string Rotation = "向き";
        private const string Parts = "ボーン";

        [Display(GroupName = Move, Name = "動き", Description = "ファイルに入っている動きから選びます")]
        [MotionSelector]
        [ShowPropertyEditorWhen(nameof(HasMotion), true)]
        public string Motion { get => motion; set => Set(ref motion, value ?? string.Empty); }
        private string motion = string.Empty;

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
        public bool MotionLoops { get => motionLoops; set => Set(ref motionLoops, value); }
        private bool motionLoops = true;

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
        public ImmutableList<ModelBonePose> Bones { get => bones; set => Set(ref bones, value); }
        private ImmutableList<ModelBonePose> bones = [];

        public Model3DItemParameter() => SubscribeChildUndoRedoable(Bones);

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal string ModelFile
        {
            get => modelFile;
            set
            {
                if (modelFile == value)
                    return;

                modelFile = value ?? string.Empty;
                OnPropertyChanged(nameof(ModelFile));
                OnPropertyChanged(nameof(HasMotion));
                OnPropertyChanged(nameof(HasBones));
            }
        }
        private string modelFile = string.Empty;

        internal ModelData? Model => ModelLibrary.Find(ModelFile);

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasMotion => Model is { Animations.Length: > 0 };

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasBones => Model is { HasBones: true };

        internal Matrix4x4[]? Sample(ModelData model, in FrameContext time)
            => ModelMotion.Sample(
                model,
                ModelMotion.Find(model, Motion),
                time,
                MotionSpeed.GetFloat(time),
                MotionStart.GetFloat(time),
                MotionLoops);

        ModelData? IModelHost.Model => Model;

        public ModelBonePose BoneFor(string name)
        {
            foreach (var bone in Bones)
            {
                if (bone.Name == name)
                    return bone;
            }

            var made = new ModelBonePose(name);

            Bones = Bones.Add(made);
            SubscribeChildUndoRedoable(Bones);

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

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [RotationX, RotationY, RotationZ, MotionSpeed, MotionStart, .. Bones];
    }

    internal readonly record struct TachiePose(float RotationX, float RotationY, float RotationZ)
    {
        public static TachiePose None => new(0f, 0f, 0f);

        public static TachiePose Of(ITachieItemParameter? parameter, in FrameContext time)
        {
            if (parameter is not Model3DItemParameter item)
                return None;

            return new TachiePose(
                item.RotationX.GetFloat(time),
                item.RotationY.GetFloat(time),
                item.RotationZ.GetFloat(time));
        }
    }
}
