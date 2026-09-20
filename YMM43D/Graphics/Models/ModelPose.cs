using System.Numerics;

namespace YMM43D.Graphics.Models
{
    // 骨の姿勢から、頂点を動かすための行列を作る。
    // 何も指定しなければ基本の姿勢（骨を動かしていない状態）になる。
    public static class ModelPose
    {
        public static Matrix4x4[] Skin(ModelData model, IReadOnlyList<Matrix4x4>? localRotations = null)
            => Skin(model.Bones, model.Root, localRotations);

        public static Matrix4x4[] Skin(
            ModelBone[] bones, in Matrix4x4 root, IReadOnlyList<Matrix4x4>? localRotations = null)
        {
            var skin = new Matrix4x4[bones.Length];

            if (bones.Length == 0)
                return skin;

            var globals = new Matrix4x4[bones.Length];
            var done = new bool[bones.Length];

            for (var i = 0; i < bones.Length; i++)
                Global(bones, localRotations, globals, done, i, 0);

            for (var i = 0; i < bones.Length; i++)
                skin[i] = bones[i].InverseBind * globals[i] * root;

            return skin;
        }

        private static Matrix4x4 Global(
            ModelBone[] bones,
            IReadOnlyList<Matrix4x4>? localRotations,
            Matrix4x4[] globals,
            bool[] done,
            int index,
            int depth)
        {
            if (done[index])
                return globals[index];

            // 親をたどる途中で輪になっていたら、そこで打ち切る。
            done[index] = true;
            globals[index] = Matrix4x4.Identity;

            var bone = bones[index];

            var local = localRotations is { } rotations && index < rotations.Count
                ? rotations[index] * bone.Local
                : bone.Local;

            var parent = bone.Parent;

            return globals[index] = parent >= 0 && parent < bones.Length && depth < bones.Length
                ? local * Global(bones, localRotations, globals, done, parent, depth + 1)
                : local;
        }
    }
}
