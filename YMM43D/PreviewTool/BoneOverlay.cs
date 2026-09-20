using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    // 3Dプレビューに出す骨 1 本ぶん。掴んで回すのに要るものをまとめて持つ。
    internal readonly record struct PreviewBone(
        IVideoItem Item,
        Model3DParameter Parameter,
        FrameContext Time,
        int Index,
        string Name,
        Vector3 Position,
        Vector3 ParentPosition,
        bool HasParent,
        Matrix4x4 Frame)
    {
        // 回転X/Y/Z の各つまみが向いている先。オイラー角の順（X→Y→Z）に合わせる。
        public Matrix4x4 HandleOrientation(float x, float y, float z)
        {
            var second = Matrix4x4.CreateRotationX(float.DegreesToRadians(x)) * Frame;
            var third = Matrix4x4.CreateRotationY(float.DegreesToRadians(y))
                      * Matrix4x4.CreateRotationX(float.DegreesToRadians(x)) * Frame;

            var made = Matrix4x4.Identity;

            SetRow(ref made, 0, Normalized(Frame, 0));
            SetRow(ref made, 1, Normalized(second, 1));
            SetRow(ref made, 2, Normalized(third, 2));

            return made;
        }

        private static Vector3 Normalized(in Matrix4x4 matrix, int row)
        {
            var value = row switch
            {
                0 => new Vector3(matrix.M11, matrix.M12, matrix.M13),
                1 => new Vector3(matrix.M21, matrix.M22, matrix.M23),
                _ => new Vector3(matrix.M31, matrix.M32, matrix.M33),
            };

            return value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : value;
        }

        private static void SetRow(ref Matrix4x4 matrix, int row, in Vector3 value)
        {
            switch (row)
            {
                case 0: matrix.M11 = value.X; matrix.M12 = value.Y; matrix.M13 = value.Z; break;
                case 1: matrix.M21 = value.X; matrix.M22 = value.Y; matrix.M23 = value.Z; break;
                default: matrix.M31 = value.X; matrix.M32 = value.Y; matrix.M33 = value.Z; break;
            }
        }
    }

    // 選んでいる 3Dモデルの骨を、いまの姿勢のまま 3D 空間の位置に並べる。
    internal static class BoneOverlay
    {
        public static IReadOnlyList<PreviewBone> Resolve(
            IVideoItem item, in FrameContext itemTime, in Matrix4x4 itemWorld)
        {
            if (item is not ShapeItem { ShapeParameter: Model3DParameter parameter }
                || parameter.Model is not { HasBones: true } model)
            {
                return [];
            }

            var pose = parameter.GetBoneFrames(model, itemTime);
            var world = parameter.GetLocalMatrix(model, itemTime) * itemWorld;

            var bones = new PreviewBone[model.Bones.Length];

            for (var i = 0; i < model.Bones.Length; i++)
            {
                var bone = model.Bones[i];
                var frame = pose.Frames[i] * model.Root * world;
                var position = Vector3.Transform(Vector3.Zero, pose.Globals[i] * model.Root * world);

                var parent = bone.Parent;
                var hasParent = parent >= 0 && parent < model.Bones.Length;

                var parentPosition = hasParent
                    ? Vector3.Transform(Vector3.Zero, pose.Globals[parent] * model.Root * world)
                    : position;

                bones[i] = new PreviewBone(
                    item, parameter, itemTime, i, bone.Name, position, parentPosition, hasParent, frame);
            }

            return bones;
        }
    }
}
