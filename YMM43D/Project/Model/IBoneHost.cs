using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;

namespace YMM43D.Project.Model
{
    internal interface IModelHost
    {
        ModelData? Model { get; }
    }

    internal interface IBoneHost : IModelHost
    {
        ModelBonePose BoneFor(string name);

        void DropBoneIfUntouched(string name);
    }

    internal static class ModelMotion
    {
        public static ModelAnimation? Find(ModelData model, string name)
        {
            if (name.Length == 0)
                return null;

            foreach (var one in model.Animations)
            {
                if (one.Name == name)
                    return one;
            }

            return null;
        }

        public static float SecondsOf(
            ModelAnimation motion, in FrameContext time, float speed, float start, bool loops)
        {
            var fps = Math.Max(1, time.Fps);
            var at = time.Frame / (float)fps * (speed / 100f) + start;

            if (!loops || motion.Duration <= 0f)
                return at;

            at %= motion.Duration;

            return at < 0f ? at + motion.Duration : at;
        }

        public static Matrix4x4[]? Sample(
            ModelData model,
            ModelAnimation? motion,
            in FrameContext time,
            float speed,
            float start,
            bool loops)
        {
            if (motion is null)
                return null;

            var pose = BonePoses.Identity(model.Bones.Length);

            ModelAnimationSampler.Sample(motion, SecondsOf(motion, time, speed, start, loops), pose, null);

            return pose;
        }
    }

    internal static class BonePoses
    {
        public static Matrix4x4[]? Apply(
            ModelData model,
            IReadOnlyList<ModelBonePose> poses,
            in FrameContext time,
            Matrix4x4[]? onto)
        {
            foreach (var bone in poses)
            {
                if (bone.Name.Length == 0 || bone.IsStill(time))
                    continue;

                var rotation = bone.GetRotation(time);

                for (var i = 0; i < model.Bones.Length; i++)
                {
                    if (model.Bones[i].Name != bone.Name)
                        continue;

                    onto ??= Identity(model.Bones.Length);
                    onto[i] = rotation * onto[i];
                }
            }

            return onto;
        }

        public static Matrix4x4[] Identity(int count)
        {
            var made = new Matrix4x4[count];

            for (var i = 0; i < count; i++)
                made[i] = Matrix4x4.Identity;

            return made;
        }
    }
}
