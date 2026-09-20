using System.Numerics;

namespace YMM43D.Graphics.Models
{
    // 骨の姿勢から、頂点を動かすための行列を作る。
    // 何も指定しなければ基本の姿勢（骨を動かしていない状態）になる。
    public static class ModelPose
    {
        // Frames は回転を掛ける前の骨の置き場所、Globals は回転まで掛けたあとの置き場所。
        public readonly record struct PoseFrames(Matrix4x4[] Frames, Matrix4x4[] Globals);

        public static Matrix4x4[] Skin(ModelData model, IReadOnlyList<Matrix4x4>? localRotations = null)
            => Skin(model.Bones, model.Root, localRotations);

        public static Matrix4x4[] Skin(
            ModelBone[] bones, in Matrix4x4 root, IReadOnlyList<Matrix4x4>? localRotations = null)
        {
            var pose = Compute(bones, localRotations);
            var skin = new Matrix4x4[bones.Length];

            for (var i = 0; i < bones.Length; i++)
                skin[i] = bones[i].InverseBind * pose.Globals[i] * root;

            return skin;
        }

        public static PoseFrames Compute(ModelBone[] bones, IReadOnlyList<Matrix4x4>? localRotations = null)
        {
            var frames = new Matrix4x4[bones.Length];
            var globals = new Matrix4x4[bones.Length];
            var done = new bool[bones.Length];

            for (var i = 0; i < bones.Length; i++)
                Global(bones, localRotations, frames, globals, done, i, 0);

            return new PoseFrames(frames, globals);
        }

        private static Matrix4x4 Global(
            ModelBone[] bones,
            IReadOnlyList<Matrix4x4>? localRotations,
            Matrix4x4[] frames,
            Matrix4x4[] globals,
            bool[] done,
            int index,
            int depth)
        {
            if (done[index])
                return globals[index];

            // 親をたどる途中で輪になっていたら、そこで打ち切る。
            done[index] = true;
            frames[index] = Matrix4x4.Identity;
            globals[index] = Matrix4x4.Identity;

            var bone = bones[index];
            var parent = bone.Parent;

            var frame = parent >= 0 && parent < bones.Length && depth < bones.Length
                ? bone.Local * Global(bones, localRotations, frames, globals, done, parent, depth + 1)
                : bone.Local;

            frames[index] = frame;

            var rotation = localRotations is { } rotations && index < rotations.Count
                ? rotations[index]
                : Matrix4x4.Identity;

            return globals[index] = rotation * frame;
        }
    }
}
