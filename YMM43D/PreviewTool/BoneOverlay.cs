using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Project.Model;
using YMM43D.Project.Tachie;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct PreviewBone(
        IVideoItem Item,
        IBoneHost Parameter,
        FrameContext Time,
        int Index,
        string Name,
        string ParentName,
        Vector3 Position,
        Vector3 Tail,
        Vector3 ParentTail,
        bool HasParent,
        bool HasChildren,
        Matrix4x4 Frame)
    {
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

    internal static class BoneOverlay
    {
        public static IReadOnlyList<PreviewBone> Resolve(
            IVideoItem item, in FrameContext itemTime, in Matrix4x4 itemWorld)
        {
            if (Posed(item, itemTime) is not { } found)
                return [];

            var (parameter, model, pose, local) = found;
            var world = local * itemWorld;

            var bones = new PreviewBone[model.Bones.Length];
            var positions = new Vector3[model.Bones.Length];

            for (var i = 0; i < model.Bones.Length; i++)
                positions[i] = Vector3.Transform(Vector3.Zero, pose.Globals[i] * model.Root * world);

            var tails = Tails(model.Bones, positions);

            for (var i = 0; i < model.Bones.Length; i++)
            {
                var bone = model.Bones[i];
                var frame = pose.Frames[i] * model.Root * world;

                var parent = bone.Parent;
                var hasParent = parent >= 0 && parent < model.Bones.Length;

                bones[i] = new PreviewBone(
                    item, parameter, itemTime, i, bone.Name, hasParent ? model.Bones[parent].Name : string.Empty,
                    positions[i], tails[i].Tail, hasParent ? tails[parent].Tail : positions[i],
                    hasParent, tails[i].HasChildren, frame);
            }

            return bones;
        }

        private static (IBoneHost Host, ModelData Model, ModelPose.PoseFrames Pose, Matrix4x4 Local)? Posed(
            IVideoItem item, in FrameContext itemTime)
        {
            if (item is ShapeItem { ShapeParameter: Model3DParameter parameter })
            {
                return parameter.Model is { HasBones: true } model
                    ? (parameter, model, parameter.GetBoneFrames(model, itemTime),
                        parameter.GetLocalMatrix(model, itemTime))
                    : null;
            }

            if (item is not TachieItem
                {
                    Character.TachieCharacterParameter: Model3DCharacterParameter character,
                    TachieItemParameter: Model3DItemParameter standing,
                })
            {
                return null;
            }

            if (ModelLibrary.Find(character.File) is not { HasBones: true } shown)
                return null;

            var look = new TachieLook(shown, character, [], standing, null, TimeSpan.Zero, []);

            return (
                standing,
                shown,
                ModelPose.Compute(
                    shown.Bones,
                    BonePoses.Apply(shown, standing.Bones, itemTime, standing.Sample(shown, itemTime))),
                look.GetLocalMatrix(shown, itemTime));
        }

        private static (Vector3 Tail, bool HasChildren)[] Tails(ModelBone[] bones, Vector3[] positions)
        {
            var spread = Spread(positions);
            var same = MathF.Max(spread * 1e-4f, 1e-6f);
            var fallback = MathF.Max(spread * 0.05f, 1e-5f);

            var made = new (Vector3 Tail, bool HasChildren)[bones.Length];
            var directions = new Vector3[bones.Length];

            var children = new List<int>[bones.Length];

            for (var i = 0; i < bones.Length; i++)
                children[i] = [];

            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i].Parent >= 0 && bones[i].Parent < bones.Length)
                    children[bones[i].Parent].Add(i);
            }

            for (var i = 0; i < bones.Length; i++)
            {
                var reach = children[i]
                    .Where(child => Vector3.Distance(positions[child], positions[i]) > same)
                    .ToArray();

                var parent = bones[i].Parent;
                var hasParent = parent >= 0 && parent < bones.Length;

                var flow = hasParent && Vector3.Distance(positions[i], positions[parent]) > same
                    ? Vector3.Normalize(positions[i] - positions[parent])
                    : hasParent && directions[parent].LengthSquared() > 0f
                        ? directions[parent]
                        : Vector3.UnitY;

                if (reach.Length == 1)
                {
                    made[i] = (positions[reach[0]], true);
                    directions[i] = Vector3.Normalize(positions[reach[0]] - positions[i]);

                    continue;
                }

                var nearest = reach.Length > 0
                    ? reach.Min(child => Vector3.Distance(positions[child], positions[i]))
                    : hasParent && Vector3.Distance(positions[i], positions[parent]) > same
                        ? Vector3.Distance(positions[i], positions[parent])
                        : fallback;

                made[i] = (positions[i] + flow * (nearest * 0.25f), reach.Length > 0);
                directions[i] = flow;
            }

            return made;
        }

        private static float Spread(Vector3[] positions)
        {
            if (positions.Length == 0)
                return 0f;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var position in positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var extent = max - min;

            return MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        }
    }
}
