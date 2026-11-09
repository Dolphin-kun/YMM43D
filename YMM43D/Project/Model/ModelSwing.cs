using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;

namespace YMM43D.Project.Model
{
    internal sealed class ModelSwing
    {
        private readonly ModelSpringState springs = new();

        private ModelData? ownSpringsFor;
        private string[] ownSpringNames = [];
        private IReadOnlyList<ModelSpring> ownSprings = [];

        public void Forget() => springs.Forget();

        public Matrix4x4[]? Apply(
            ModelData model,
            IReadOnlyList<ModelBonePose> bones,
            Matrix4x4[]? pose,
            in FrameContext time,
            Matrix4x4? world,
            float softness,
            float sway)
        {
            var swinging = SpringsOf(model, bones);

            if (swinging.Count == 0)
                return pose;

            if (world is not { } place)
                return springs.Last(model, pose) ?? pose;

            return springs.Apply(
                model, swinging, pose, time.Frame, 1f / Math.Max(1, time.Fps), place, softness, sway);
        }

        private IReadOnlyList<ModelSpring> SpringsOf(ModelData model, IReadOnlyList<ModelBonePose> bones)
        {
            var count = 0;

            foreach (var bone in bones)
            {
                if (Chosen(bone))
                    count++;
            }

            if (count == 0)
                return model.Springs;

            if (ReferenceEquals(ownSpringsFor, model) && SameNames(bones, count))
                return ownSprings;

            var chosen = new string[count];
            var at = 0;

            foreach (var bone in bones)
            {
                if (Chosen(bone))
                    chosen[at++] = bone.Name;
            }

            var made = new List<ModelSpring>(model.Springs);

            foreach (var name in chosen)
            {
                for (var i = 0; i < model.Bones.Length; i++)
                {
                    if (model.Bones[i].Name != name)
                        continue;

                    var spring = ModelSpring.From(
                        model.Bones,
                        i,
                        ModelSpring.DefaultStiffness,
                        ModelSpring.DefaultDrag,
                        default,
                        ModelSpring.DefaultHitRadius,
                        model.Colliders);

                    if (spring is not null)
                        made.Add(spring);
                }
            }

            ownSpringsFor = model;
            ownSpringNames = chosen;
            springs.Forget();

            return ownSprings = made;
        }

        private static bool Chosen(ModelBonePose bone) => bone.Swings && bone.Name.Length > 0;

        private bool SameNames(IReadOnlyList<ModelBonePose> bones, int count)
        {
            if (ownSpringNames.Length != count)
                return false;

            var at = 0;

            foreach (var bone in bones)
            {
                if (Chosen(bone) && ownSpringNames[at++] != bone.Name)
                    return false;
            }

            return true;
        }
    }
}
