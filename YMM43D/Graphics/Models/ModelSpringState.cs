using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public sealed class ModelSpringState
    {
        private const int MaxCatchUp = 30;

        private const float StepReach = 0.5f;

        private const int MaxSubSteps = 8;

        private const float StepSeconds = 1f / 60f;

        private const float JumpReach = 8f;

        private ModelData? source;
        private Vector3[]? tails;
        private Vector3[]? previous;
        private int lastFrame = int.MinValue;
        private Matrix4x4[]? result;
        private Matrix4x4[]? lastPose;
        private Matrix4x4[]? settled;
        private Matrix4x4 lastWorld = Matrix4x4.Identity;

        public Matrix4x4[]? Apply(
            ModelData model,
            IReadOnlyList<ModelSpring> springs,
            Matrix4x4[]? pose,
            int frame,
            float seconds,
            in Matrix4x4 world,
            float softness = 1f,
            float sway = 1f)
        {
            if (springs.Count == 0 || seconds <= 0f)
                return pose;

            var count = springs.Sum(spring => spring.Joints.Length);

            if (!ReferenceEquals(source, model) || tails is null || tails.Length != count)
            {
                source = model;
                tails = new Vector3[count];
                previous = new Vector3[count];
                lastFrame = int.MinValue;
            }

            if (frame == lastFrame && result is not null)
            {
                if (Same(pose, lastPose))
                    return result;

                lastFrame = int.MinValue;
            }

            Remember(pose);

            var steps = frame - lastFrame;

            if (Jumped(model, springs, world))
                steps = 0;

            if (steps is < 1 or > MaxCatchUp)
            {
                lastFrame = frame;
                lastWorld = world;

                return result = Settle(model, springs, pose, seconds, world, softness, sway, rest: true);
            }

            for (var i = 0; i < steps; i++)
            {
                var parts = Math.Max(
                    SubSteps(model, springs, world, (i + 1f) / steps),
                    Math.Clamp((int)MathF.Ceiling(seconds / StepSeconds), 1, MaxSubSteps));
                var from = (i + 0f) / steps;
                var span = 1f / steps;

                for (var part = 1; part <= parts; part++)
                {
                    var place = Matrix4x4.Lerp(lastWorld, world, from + span * part / parts);

                    result = Settle(
                        model, springs, pose, seconds / parts, place, softness, sway, rest: false);
                }
            }

            lastFrame = frame;
            lastWorld = world;

            return result;
        }

        public void Forget()
        {
            lastFrame = int.MinValue;
            lastPose = null;
        }

        public Matrix4x4[]? Last(ModelData model, Matrix4x4[]? pose)
            => ReferenceEquals(source, model) && Same(pose, lastPose) ? result : null;

        private void Remember(Matrix4x4[]? pose)
        {
            if (pose is null)
            {
                lastPose = null;

                return;
            }

            if (lastPose is null || lastPose.Length != pose.Length)
                lastPose = new Matrix4x4[pose.Length];

            pose.AsSpan().CopyTo(lastPose);
        }

        private static bool Same(Matrix4x4[]? one, Matrix4x4[]? other)
        {
            if (one is null || other is null)
                return one is null && other is null;

            if (one.Length != other.Length)
                return false;

            for (var i = 0; i < one.Length; i++)
            {
                if (one[i] != other[i])
                    return false;
            }

            return true;
        }

        private bool Jumped(ModelData model, IReadOnlyList<ModelSpring> springs, in Matrix4x4 world)
        {
            if (lastFrame == int.MinValue)
                return false;

            var moved = Vector3.Distance(world.Translation, lastWorld.Translation);

            if (moved < 1e-6f)
                return false;

            var longest = 0f;

            foreach (var spring in springs)
            {
                foreach (var joint in spring.Joints)
                    longest = MathF.Max(longest, joint.Length);
            }

            if (longest <= 1e-6f)
                return false;

            var scale = ScaleOf(model.Root * world);

            return moved > longest * MathF.Max(scale, 1e-3f) * JumpReach;
        }

        private int SubSteps(
            ModelData model, IReadOnlyList<ModelSpring> springs, in Matrix4x4 world, float share)
        {
            var moved = Vector3.Distance(world.Translation, lastWorld.Translation) * share;

            if (moved < 1e-6f)
                return 1;

            var shortest = float.MaxValue;

            foreach (var spring in springs)
            {
                foreach (var joint in spring.Joints)
                    shortest = MathF.Min(shortest, joint.Length);
            }

            if (!float.IsFinite(shortest) || shortest <= 1e-6f)
                return 1;

            var scale = ScaleOf(model.Root * world);
            var reach = MathF.Max(shortest * scale * StepReach, 1e-4f);

            return Math.Clamp((int)MathF.Ceiling(moved / reach), 1, MaxSubSteps);
        }

        private static float ScaleOf(in Matrix4x4 place)
            => new Vector3(place.M11, place.M12, place.M13).Length();

        public Matrix4x4[]? Last(ModelData model) => ReferenceEquals(source, model) ? result : null;

        private Matrix4x4[] Settle(
            ModelData model,
            IReadOnlyList<ModelSpring> springs,
            Matrix4x4[]? pose,
            float seconds,
            in Matrix4x4 world,
            float softness,
            float sway,
            bool rest)
        {
            if (settled is null || settled.Length != model.Bones.Length)
                settled = new Matrix4x4[model.Bones.Length];

            var made = settled;

            for (var i = 0; i < made.Length; i++)
                made[i] = pose is not null && i < pose.Length ? pose[i] : Matrix4x4.Identity;

            var globals = ModelPose.Compute(model.Bones, made).Globals;
            var at = 0;

            var stage = model.Root * world;

            if (!Matrix4x4.Invert(stage, out var back))
                return made;

            foreach (var spring in springs)
            {
                foreach (var joint in spring.Joints)
                {
                    var place = globals[joint.Bone] * stage;
                    var head = place.Translation;

                    var reach = Vector3.TransformNormal(joint.Axis * joint.Length, place);

                    if (reach.LengthSquared() < 1e-12f)
                    {
                        at++;

                        continue;
                    }

                    var length = reach.Length();
                    var axis = reach / length;

                    if (rest)
                    {
                        tails![at] = previous![at] = head + reach;
                        at++;

                        continue;
                    }

                    var scale = joint.Length > 1e-6f ? length / joint.Length : 1f;

                    var stiffness = joint.Stiffness / MathF.Max(softness, 0.01f);
                    var drag = Math.Clamp(joint.Drag / MathF.Max(sway, 0.01f), 0f, 1f);

                    var keep = MathF.Pow(1f - drag, seconds * 60f);

                    var moving = (tails![at] - previous![at]) * keep;
                    var pull = axis * (stiffness * seconds * scale);
                    var fall = joint.Gravity * (seconds * scale);

                    var next = tails[at] + moving + pull + fall;
                    var aside = next - head;

                    if (aside.LengthSquared() < 1e-12f)
                        aside = axis;

                    next = head + Vector3.Normalize(aside) * length;

                    if (spring.Colliders.Length > 0)
                        next = PushOut(spring.Colliders, joint, globals, stage, next, head, length);

                    previous[at] = tails[at];
                    tails[at] = next;

                    var turn = TurnBetween(axis, Vector3.Normalize(next - head));

                    var moved = place
                        * Matrix4x4.CreateTranslation(-head) * turn * Matrix4x4.CreateTranslation(head);

                    var global = moved * back;

                    if (Matrix4x4.Invert(
                        model.Bones[joint.Bone].Local * ParentOf(model, globals, joint.Bone), out var undo))
                    {
                        made[joint.Bone] = global * undo;
                    }

                    globals[joint.Bone] = global;

                    Spread(model, globals, made, joint.Bone);

                    at++;
                }
            }

            return made;
        }

        private static Vector3 PushOut(
            ModelSpringCollider[] colliders,
            in ModelSpringJoint joint,
            Matrix4x4[] globals,
            in Matrix4x4 stage,
            Vector3 tail,
            in Vector3 head,
            float length)
        {
            foreach (var collider in colliders)
            {
                if (collider.Bone < 0 || collider.Bone >= globals.Length || collider.Radius <= 0f)
                    continue;

                var place = globals[collider.Bone] * stage;
                var scale = ScaleOf(place);
                var centre = Vector3.Transform(collider.Offset, place);

                var near = collider.IsCapsule
                    ? ModelSpring.Nearest(centre, Vector3.Transform(collider.Tail, place), tail)
                    : centre;

                var apart = tail - near;

                var keep = MathF.Max(collider.Radius + joint.HitRadius - joint.Slack, 0f) * scale;
                var span = apart.Length();

                if (keep <= 0f)
                    continue;

                if (span >= keep)
                    continue;

                tail = span > 1e-9f
                    ? near + apart / span * keep
                    : near + Vector3.Normalize(tail - head + Vector3.UnitY * 1e-6f) * keep;

                var aside = tail - head;

                if (aside.LengthSquared() < 1e-12f)
                    continue;

                tail = head + Vector3.Normalize(aside) * length;
            }

            return tail;
        }

        private static Matrix4x4 ParentOf(ModelData model, Matrix4x4[] globals, int bone)
        {
            var parent = model.Bones[bone].Parent;

            return parent >= 0 && parent < globals.Length ? globals[parent] : Matrix4x4.Identity;
        }

        private static void Spread(ModelData model, Matrix4x4[] globals, Matrix4x4[] pose, int bone)
        {
            for (var i = 0; i < model.Bones.Length; i++)
            {
                if (model.Bones[i].Parent != bone)
                    continue;

                globals[i] = pose[i] * model.Bones[i].Local * globals[bone];

                Spread(model, globals, pose, i);
            }
        }

        private static Matrix4x4 TurnBetween(in Vector3 a, in Vector3 b)
        {
            var axis = Vector3.Cross(a, b);
            var dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);

            if (axis.LengthSquared() < 1e-12f)
            {
                if (dot > 0f)
                    return Matrix4x4.Identity;

                var side = MathF.Abs(a.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;

                axis = Vector3.Cross(a, side);
            }

            return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot));
        }
    }
}
