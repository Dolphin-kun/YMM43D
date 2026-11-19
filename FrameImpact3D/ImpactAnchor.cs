using System.Numerics;

namespace FrameImpact3D
{
    internal readonly record struct ImpactAnchor(Vector3 Center, Vector3 AxisX, Vector3 AxisY, float Scale)
    {
        public static ImpactAnchor From(in Matrix4x4 placement, Vector3 center)
            => new(Vector3.Transform(center, placement), Direction(Vector3.UnitX, placement, Vector3.UnitX), Direction(Vector3.UnitY, placement, Vector3.UnitY), ScaleOf(placement));

        public static float ScaleOf(in Matrix4x4 placement)
            => (Vector3.TransformNormal(Vector3.UnitX, placement).Length() + Vector3.TransformNormal(Vector3.UnitY, placement).Length()) / 2f;

        private static Vector3 Direction(Vector3 axis, in Matrix4x4 placement, Vector3 fallback)
        {
            var moved = Vector3.TransformNormal(axis, placement);

            return moved.LengthSquared() > 1e-12f ? Vector3.Normalize(moved) : fallback;
        }
    }
}
