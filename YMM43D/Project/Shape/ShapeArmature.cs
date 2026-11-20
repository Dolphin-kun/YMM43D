using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics.Models;
using YMM43D.Project.Model;

namespace YMM43D.Project.Shape
{
    internal static class ShapeArmature
    {
        private const float Falloff = 8f;
        private const float Sideways = 0.35f;
        private const float Reach = 1e-3f;
        private const int SmoothPasses = 4;
        private const float Floor = 0.02f;

        public static ModelData? Build(IReadOnlyList<ShapeBone> bones)
        {
            if (bones.Count == 0)
                return null;

            var rest = bones.Select(Rest).ToArray();
            var made = new ModelBone[bones.Count];

            for (var i = 0; i < bones.Count; i++)
            {
                var parent = ParentOf(bones, i);
                var local = parent >= 0 && Matrix4x4.Invert(rest[parent], out var back) ? rest[i] * back : rest[i];

                made[i] = new ModelBone(bones[i].Name, parent, local, Matrix4x4.Invert(rest[i], out var inverse) ? inverse : Matrix4x4.Identity);
            }

            return new ModelData([], [], [], [], [], made);
        }

        public static Matrix4x4[] Skin(ModelData model, IReadOnlyList<ModelBonePose> poses, in FrameContext time)
            => ModelPose.Skin(model, BonePoses.Apply(model, poses, time, null));

        public static float[][] AutoWeights(MeshTopology mesh, IReadOnlyList<ShapeBone> bones)
        {
            var weights = bones.Select(_ => new float[mesh.Points.Length]).ToArray();

            for (var p = 0; p < mesh.Points.Length; p++)
            {
                for (var b = 0; b < bones.Count; b++)
                    weights[b][p] = bones[b].Deforms ? 1f / MathF.Pow(Distance(mesh.Points[p], bones[b].Head, bones[b].Tail) + Reach, Falloff) : 0f;
            }

            Normalize(weights, mesh.Points.Length);

            for (var pass = 0; pass < SmoothPasses; pass++)
            {
                foreach (var weight in weights)
                {
                    var before = (float[])weight.Clone();

                    for (var p = 0; p < before.Length; p++)
                    {
                        var edges = mesh.VertexEdges[p];

                        if (edges.Length == 0)
                            continue;

                        var sum = 0f;

                        foreach (var edge in edges)
                            sum += before[mesh.Edges[edge].Other(p)];

                        weight[p] = (before[p] + sum / edges.Length) / 2f;
                    }
                }

                Normalize(weights, mesh.Points.Length);
            }

            foreach (var weight in weights)
            {
                for (var p = 0; p < weight.Length; p++)
                {
                    if (weight[p] < Floor)
                        weight[p] = 0f;
                }
            }

            Normalize(weights, mesh.Points.Length);

            return weights;
        }

        public static MeshTopology Deformed(MeshTopology mesh, Matrix4x4[] skin)
        {
            if (mesh.Weights is not { } weights || weights.Length != skin.Length)
                return mesh;

            var points = new Vector3[mesh.Points.Length];

            for (var p = 0; p < points.Length; p++)
            {
                var point = mesh.Points[p];
                var moved = Vector3.Zero;
                var total = 0f;

                for (var b = 0; b < skin.Length; b++)
                {
                    var weight = weights[b][p];

                    if (weight <= 0f)
                        continue;

                    moved += Vector3.Transform(point, skin[b]) * weight;
                    total += weight;
                }

                points[p] = total > 1e-6f ? moved / total : point;
            }

            return mesh.WithPoints(points);
        }

        private static void Normalize(float[][] weights, int count)
        {
            for (var p = 0; p < count; p++)
            {
                var sum = 0f;

                foreach (var weight in weights)
                    sum += weight[p];

                if (sum <= 0f)
                    continue;

                foreach (var weight in weights)
                    weight[p] /= sum;
            }
        }

        private static float Distance(Vector3 point, Vector3 head, Vector3 tail)
        {
            var span = tail - head;
            var length = span.Length();

            if (length < 1e-6f)
                return Vector3.Distance(point, head);

            var axis = span / length;
            var along = Vector3.Dot(point - head, axis);
            var outside = along < 0f ? -along : along > length ? along - length : 0f;
            var sideways = Vector3.Distance(point, head + axis * along) * Sideways;

            return MathF.Sqrt(outside * outside + sideways * sideways);
        }

        private static int ParentOf(IReadOnlyList<ShapeBone> bones, int index)
        {
            var name = bones[index].Parent;

            if (name.Length == 0)
                return -1;

            for (var i = 0; i < bones.Count; i++)
            {
                if (i != index && bones[i].Name == name)
                    return i;
            }

            return -1;
        }

        private static Matrix4x4 Rest(ShapeBone bone)
        {
            var head = bone.Head;
            var span = bone.Tail - head;
            var y = span.LengthSquared() > 1e-12f ? Vector3.Normalize(span) : Vector3.UnitY;
            var side = MathF.Abs(Vector3.Dot(y, Vector3.UnitZ)) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;
            var x = Vector3.Normalize(Vector3.Cross(y, side));
            var z = Vector3.Cross(x, y);

            return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, head.X, head.Y, head.Z, 1f);
        }
    }
}
