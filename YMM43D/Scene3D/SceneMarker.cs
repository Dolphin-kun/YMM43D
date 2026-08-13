using System.Numerics;

namespace YMM43D.Scene3D
{
    public enum MarkerKind
    {
        DirectionalLight,

        PointLight,

        Environment,
    }

    public readonly record struct SceneMarker(
        MarkerKind Kind,
        Vector3 Position,
        Vector3 Direction,
        float Reach,
        bool IsMovable)
    {
        public const float DirectionalDistance = 4f;

        public const float BodyRadius = 0.35f;

        public static SceneMarker ForDirectionalLight(in Vector3 toLight)
            => new(MarkerKind.DirectionalLight, toLight * DirectionalDistance, -toLight, 0f, true);

        public static SceneMarker ForPointLight(in Vector3 position, float reach)
            => new(MarkerKind.PointLight, position, Vector3.Zero, reach, true);

        public static SceneMarker ForEnvironment()
            => new(MarkerKind.Environment, Vector3.Zero, Vector3.Zero, 0f, false);
    }

    public interface ISceneMarkerSource
    {
        SceneMarker GetMarker(in FrameContext itemTime);

        void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope);
    }
}
