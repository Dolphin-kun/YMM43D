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
}
