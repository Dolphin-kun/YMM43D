using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelAnimatedBone(int Bone, Vector3 Translation, Quaternion Rotation, Vector3 Scale, Matrix4x4 RestInverse)
    {
        public Vector3 Angles { get; init; }

        public ModelJoint? Joint { get; init; }

        public Matrix4x4? Facing { get; init; }
    }
}
