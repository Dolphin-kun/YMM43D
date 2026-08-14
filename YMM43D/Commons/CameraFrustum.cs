using System.Numerics;

namespace YMM43D.Commons
{
    public static class CameraFrustum
    {
        public const float Depth = 1.2f;

        private const float PeakHeight = 1.4f;

        public static Vector3[] LocalCorners(in Vector2 tangent)
        {
            var half = tangent * Depth;

            return
            [
                new(-half.X, half.Y, -Depth),
                new(half.X, half.Y, -Depth),
                new(half.X, -half.Y, -Depth),
                new(-half.X, -half.Y, -Depth),
            ];
        }

        public static Vector3 LocalPeak(in Vector2 tangent)
            => new(0f, tangent.Y * Depth * PeakHeight, -Depth);

        public static Vector3[] WorldCorners(in CameraPose pose, in Vector2 tangent)
        {
            var world = pose.WorldMatrix;
            var corners = LocalCorners(tangent);

            for (var i = 0; i < corners.Length; i++)
                corners[i] = Vector3.Transform(corners[i], world);

            return corners;
        }
    }
}
