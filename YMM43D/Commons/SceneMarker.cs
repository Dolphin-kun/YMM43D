using System.Numerics;

namespace YMM43D.Commons
{
    public enum MarkerKind
    {
        DirectionalLight,

        PointLight,

        Camera,
    }

    public readonly record struct SceneMarker(
        MarkerKind Kind,
        Vector3 Position,
        Vector3 Direction,
        float Reach)
    {
        public const float DirectionalDistance = 4f;

        public const float BodyRadius = 0.35f;

        public static SceneMarker ForDirectionalLight(in Vector3 toLight)
            => new(MarkerKind.DirectionalLight, toLight * DirectionalDistance, -toLight, 0f);

        public static SceneMarker ForPointLight(in Vector3 position, float reach)
            => new(MarkerKind.PointLight, position, Vector3.Zero, reach);

        public static SceneMarker ForCamera(in Vector3 position)
            => new(MarkerKind.Camera, position, Vector3.Zero, 0f);
    }

    public interface ISceneMarkerSource
    {
        SceneMarker GetMarker(in FrameContext itemTime);

        void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope);
    }
}
