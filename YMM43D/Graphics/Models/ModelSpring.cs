using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelSpringJoint(
        int Bone,

        Vector3 Axis,
        float Length,

        float Stiffness,

        float Drag,

        Vector3 Gravity,

        float HitRadius = 0f,

        float Slack = 0f);

    public readonly record struct ModelSpringCollider(
        int Bone,

        Vector3 Offset,
        float Radius,

        Vector3 Tail,
        bool IsCapsule);

    public sealed record ModelSpring(ModelSpringJoint[] Joints)
    {
        public ModelSpringCollider[] Colliders { get; init; } = [];

        public const float DefaultStiffness = 1f;

        public const float DefaultDrag = 0.4f;

        public const float DefaultHitRadius = 0.02f;

        public static ModelSpring? From(
            ModelBone[] bones,
            int root,
            float stiffness = DefaultStiffness,
            float drag = DefaultDrag,
            Vector3 gravity = default,
            float hitRadius = 0f,
            ModelSpringCollider[]? colliders = null)
        {
            var joints = new List<ModelSpringJoint>();
            var waiting = new Queue<int>();

            waiting.Enqueue(root);

            while (waiting.Count > 0 && joints.Count < bones.Length)
            {
                var bone = waiting.Dequeue();

                if (Reach(bones, bone) is { } reach)
                    joints.Add(new ModelSpringJoint(
                        bone, reach.Axis, reach.Length, stiffness, drag, gravity, hitRadius));

                for (var i = 0; i < bones.Length; i++)
                {
                    if (bones[i].Parent == bone)
                        waiting.Enqueue(i);
                }
            }

            if (joints.Count == 0)
                return null;

            return Allowing(new ModelSpring([.. joints]) { Colliders = colliders ?? [] }, bones);
        }

        public static ModelSpring Allowing(ModelSpring spring, ModelBone[] bones)
        {
            if (spring.Colliders.Length == 0)
                return spring;

            var globals = ModelPose.Compute(bones).Globals;
            var joints = new ModelSpringJoint[spring.Joints.Length];

            for (var i = 0; i < joints.Length; i++)
            {
                var joint = spring.Joints[i];

                if (joint.Bone < 0 || joint.Bone >= globals.Length)
                {
                    joints[i] = joint;

                    continue;
                }

                var place = globals[joint.Bone];
                var tip = place.Translation + Vector3.TransformNormal(joint.Axis * joint.Length, place);
                var slack = 0f;

                foreach (var collider in spring.Colliders)
                {
                    if (collider.Bone < 0 || collider.Bone >= globals.Length || collider.Radius <= 0f)
                        continue;

                    var hit = globals[collider.Bone];
                    var centre = Vector3.Transform(collider.Offset, hit);

                    var near = collider.IsCapsule
                        ? Nearest(centre, Vector3.Transform(collider.Tail, hit), tip)
                        : centre;

                    slack = MathF.Max(
                        slack, collider.Radius + joint.HitRadius - Vector3.Distance(tip, near));
                }

                joints[i] = joint with { Slack = slack };
            }

            return spring with { Joints = joints };
        }

        internal static Vector3 Nearest(in Vector3 from, in Vector3 to, in Vector3 point)
        {
            var span = to - from;
            var length = span.LengthSquared();

            if (length < 1e-12f)
                return from;

            return from + span * Math.Clamp(Vector3.Dot(point - from, span) / length, 0f, 1f);
        }

        public static (Vector3 Axis, float Length)? Reach(ModelBone[] bones, int bone)
        {
            var child = -1;

            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i].Parent != bone)
                    continue;

                child = i;
                break;
            }

            var offset = child >= 0 ? bones[child].Local.Translation : bones[bone].Local.Translation;
            var length = offset.Length();

            if (length < 1e-6f)
                return null;

            return (offset / length, child >= 0 ? length : 0.07f);
        }
    }
}
