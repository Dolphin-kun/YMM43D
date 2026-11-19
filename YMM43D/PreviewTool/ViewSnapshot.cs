using System.Numerics;
using YMM43D.Commons;

namespace YMM43D.PreviewTool
{
    internal readonly record struct ViewSnapshot(Matrix4x4 ViewProjection, float Width, float Height, Vector3 Eye, Vector3 Forward, bool Orthographic)
    {
        public Vector2? ToScreen(in Vector3 point)
        {
            if (Width <= 0f || Height <= 0f)
                return null;

            var clip = Vector4.Transform(new Vector4(point, 1f), ViewProjection);

            if (clip.W <= 1e-6f)
                return null;

            var ndc = new Vector2(clip.X, clip.Y) / clip.W;

            return new Vector2((ndc.X + 1f) * Width / 2f, (1f - ndc.Y) * Height / 2f);
        }

        public PickRay? Ray(in Vector2 screen) => PickRay.FromScreen(screen, Width, Height, ViewProjection);

        public float PixelsPerUnit(in Vector3 at)
        {
            if (ToScreen(at) is not { } center)
                return 0f;

            var across = Vector3.Cross(Forward, Vector3.UnitY);

            if (across.LengthSquared() < 1e-8f)
                across = Vector3.UnitX;

            return ToScreen(at + Vector3.Normalize(across)) is { } side ? Vector2.Distance(center, side) : 0f;
        }
    }
}
