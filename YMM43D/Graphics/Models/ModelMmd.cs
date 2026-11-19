using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public static class ModelMmd
    {
        private const float Reached = 1e-5f;
        private const float TinyAxis = 1e-8f;

        public static Matrix4x4[]? Settle(ModelData model, Matrix4x4[]? pose)
        {
            if (pose is null || !model.IsSolved || pose.Length != model.Bones.Length)
                return pose;

            var inherits = new Dictionary<int, ModelInherit>(model.Inherits.Length);

            foreach (var one in model.Inherits)
                inherits.TryAdd(one.Bone, one);

            var chains = new Dictionary<int, ModelIk>(model.Iks.Length);

            foreach (var one in model.Iks)
                chains.TryAdd(one.Bone, one);

            var order = model.BoneOrder.Length == model.Bones.Length
                ? model.BoneOrder
                : [.. Enumerable.Range(0, model.Bones.Length)];

            var walk = new Walk(model, pose);

            foreach (var bone in order)
            {
                if (inherits.TryGetValue(bone, out var inherit))
                {
                    Inherit(pose, inherit);
                    walk.Forget();
                }

                if (chains.TryGetValue(bone, out var chain))
                    Solve(pose, walk, chain);
            }

            return pose;
        }

        private static void Inherit(Matrix4x4[] pose, in ModelInherit inherit)
        {
            if (!Matrix4x4.Decompose(pose[inherit.From], out _, out var turn, out var shift))
                return;

            var added = Matrix4x4.Identity;

            if (inherit.Rotation)
            {
                added = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, turn, inherit.Weight)));
            }

            if (inherit.Translation)
                added *= Matrix4x4.CreateTranslation(shift * inherit.Weight);

            pose[inherit.Bone] = added * pose[inherit.Bone];
        }

        private static void Solve(Matrix4x4[] pose, Walk walk, ModelIk chain)
        {
            var goal = walk.Global(chain.Bone).Translation;
            var links = chain.Links;

            if (Bend(pose, walk, chain, goal))
            {
                walk.Forget();
                links = [chain.Links[1]];
            }

            for (var loop = 0; loop < chain.Loops; loop++)
            {
                var closed = true;

                foreach (var link in links)
                {
                    var tip = walk.Global(chain.Target).Translation;

                    if ((tip - goal).LengthSquared() <= Reached * Reached)
                        return;

                    var world = walk.Global(link.Bone);

                    if (!Matrix4x4.Invert(world, out var inverse))
                        continue;

                    var from = Vector3.Transform(tip, inverse);
                    var onto = Vector3.Transform(goal, inverse);

                    if (from.LengthSquared() <= TinyAxis || onto.LengthSquared() <= TinyAxis)
                        continue;

                    from = Vector3.Normalize(from);
                    onto = Vector3.Normalize(onto);

                    var hinge = link.Limited ? OnlyAxis(link) : -1;
                    var turned = hinge >= 0
                        ? Hinge(pose, link, hinge, from, onto, chain.Limit)
                        : Freely(pose, link, from, onto, chain.Limit);

                    if (!turned)
                        continue;

                    closed = false;

                    walk.Forget();
                }

                if (closed)
                    return;
            }
        }

        private static bool Bend(Matrix4x4[] pose, Walk walk, ModelIk chain, in Vector3 goal)
        {
            if (chain.Links.Length != 2)
                return false;

            var middle = chain.Links[0];
            var which = middle.Limited ? OnlyAxis(middle) : -1;

            if (which < 0)
                return false;

            var root = walk.Global(chain.Links[1].Bone).Translation;
            var joint = walk.Global(middle.Bone).Translation;
            var tip = walk.Global(chain.Target).Translation;

            var upper = (joint - root).Length();
            var lower = (tip - joint).Length();
            var span = (goal - root).Length();

            if (upper <= TinyAxis || lower <= TinyAxis)
                return false;

            var cosine = Math.Clamp((upper * upper + lower * lower - span * span) / (2f * upper * lower), -1f, 1f);
            var bend = MathF.PI - MathF.Acos(cosine);
            var sign = At(middle.Upper, which) > 0f ? 1f : -1f;

            var held = Math.Clamp(bend * sign, At(middle.Lower, which), At(middle.Upper, which));

            if (!Matrix4x4.Decompose(pose[middle.Bone], out _, out var spin, out var shift))
                return false;

            if (MathF.Abs(held - At(Angles(spin), which)) <= Reached)
                return false;

            var axis = which == 0 ? Vector3.UnitX : which == 1 ? Vector3.UnitY : Vector3.UnitZ;

            pose[middle.Bone] =
                Matrix4x4.CreateFromAxisAngle(axis, held) * Matrix4x4.CreateTranslation(shift);

            return true;
        }

        private static bool Freely(Matrix4x4[] pose, in ModelIkLink link, in Vector3 from, in Vector3 onto, float limit)
        {
            var axis = Vector3.Cross(from, onto);

            if (axis.LengthSquared() <= TinyAxis)
                return false;

            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(from, onto), -1f, 1f));

            if (limit > 0f)
                angle = MathF.Min(angle, limit);

            if (angle <= Reached)
                return false;

            pose[link.Bone] =
                Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angle) * pose[link.Bone];

            if (link.Limited)
                Clamp(pose, link);

            return true;
        }

        private static bool Hinge(Matrix4x4[] pose, in ModelIkLink link, int which, in Vector3 from, in Vector3 onto, float limit)
        {
            var axis = which == 0 ? Vector3.UnitX : which == 1 ? Vector3.UnitY : Vector3.UnitZ;

            var flat = from - axis * Vector3.Dot(from, axis);
            var wanted = onto - axis * Vector3.Dot(onto, axis);

            if (flat.LengthSquared() <= TinyAxis || wanted.LengthSquared() <= TinyAxis)
                return false;

            var turn = MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(flat, wanted)), Vector3.Dot(flat, wanted));

            if (limit > 0f)
                turn = Math.Clamp(turn, -limit, limit);

            if (!Matrix4x4.Decompose(pose[link.Bone], out _, out var spin, out var shift))
                return false;

            var now = At(Angles(spin), which);
            var held = Math.Clamp(now + turn, At(link.Lower, which), At(link.Upper, which));

            if (MathF.Abs(held - now) <= Reached)
                return false;

            pose[link.Bone] =
                Matrix4x4.CreateFromAxisAngle(axis, held) * Matrix4x4.CreateTranslation(shift);

            return true;
        }

        private static int OnlyAxis(in ModelIkLink link)
        {
            var open = 0;
            var which = -1;

            for (var i = 0; i < 3; i++)
            {
                if (At(link.Upper, i) - At(link.Lower, i) <= 1e-4f)
                    continue;

                open++;
                which = i;
            }

            return open == 1 ? which : -1;
        }

        private static float At(in Vector3 value, int which) => which == 0 ? value.X : which == 1 ? value.Y : value.Z;

        private static void Clamp(Matrix4x4[] pose, in ModelIkLink link)
        {
            if (!Matrix4x4.Decompose(pose[link.Bone], out _, out var turn, out var shift))
                return;

            var angles = Angles(turn);

            var held = new Vector3(
                Math.Clamp(angles.X, link.Lower.X, link.Upper.X),
                Math.Clamp(angles.Y, link.Lower.Y, link.Upper.Y),
                Math.Clamp(angles.Z, link.Lower.Z, link.Upper.Z));

            if (held == angles)
                return;

            pose[link.Bone] =
                Matrix4x4.CreateRotationX(held.X)
                * Matrix4x4.CreateRotationY(held.Y)
                * Matrix4x4.CreateRotationZ(held.Z)
                * Matrix4x4.CreateTranslation(shift);
        }

        private static Vector3 Angles(in Quaternion turn)
        {
            var matrix = Matrix4x4.CreateFromQuaternion(turn);
            var pitch = MathF.Asin(Math.Clamp(-matrix.M13, -1f, 1f));

            if (MathF.Abs(matrix.M13) > 0.99999f)
            {
                var sign = matrix.M13 < 0f ? 1f : -1f;

                return new Vector3(MathF.Atan2(sign * matrix.M21, matrix.M22), pitch, 0f);
            }

            return new Vector3(MathF.Atan2(matrix.M23, matrix.M33), pitch, MathF.Atan2(matrix.M12, matrix.M11));
        }

        private sealed class Walk(ModelData model, Matrix4x4[] pose)
        {
            private readonly Matrix4x4[] globals = new Matrix4x4[model.Bones.Length];
            private readonly bool[] ready = new bool[model.Bones.Length];

            public Matrix4x4 Global(int index) => Global(index, 0);

            public void Forget() => Array.Clear(ready);

            private Matrix4x4 Global(int index, int depth)
            {
                if (index < 0 || index >= globals.Length || depth > globals.Length)
                    return Matrix4x4.Identity;

                if (ready[index])
                    return globals[index];

                ready[index] = true;

                var bone = model.Bones[index];
                var frame = bone.Parent >= 0 ? bone.Local * Global(bone.Parent, depth + 1) : bone.Local;

                return globals[index] = pose[index] * frame;
            }
        }
    }
}
