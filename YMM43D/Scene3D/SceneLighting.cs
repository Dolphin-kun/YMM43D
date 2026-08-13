using System.Numerics;

namespace YMM43D.Scene3D
{
    public enum LightKind
    {
        Directional,
        Point,
    }

    public readonly record struct SceneLight(
        LightKind Kind,
        Vector3 Vector,
        Vector3 Color,
        float Reach)
    {
        public static SceneLight Directional(Vector3 direction, Vector3 color)
            => new(LightKind.Directional, Normalize(direction), color, 0f);

        public static SceneLight FromAngles(float yaw, float pitch, Vector3 color)
            => Directional(ToDirection(yaw, pitch), color);

        public static Vector3 ToDirection(float yaw, float pitch)
        {
            var y = Rotation3D.ToRadians(yaw);
            var p = Rotation3D.ToRadians(Math.Clamp(pitch, -90f, 90f));

            var flat = MathF.Cos(p);

            return new Vector3(-flat * MathF.Sin(y), MathF.Sin(p), flat * MathF.Cos(y));
        }

        public static SceneLight Point(Vector3 position, Vector3 color, float reach)
            => new(LightKind.Point, position, color, MathF.Max(reach, 0.01f));

        private static Vector3 Normalize(in Vector3 value)
            => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : new Vector3(0f, 0f, 1f);
    }

    public readonly record struct SceneFog(Vector3 Color, float Density, float Start, float End)
    {
        public static SceneFog None => new(Vector3.Zero, 0f, 0f, 1f);

        public bool IsEnabled => Density > 0f && End > Start;
    }

    public sealed class SceneLighting
    {
        public const int MaxLights = 4;

        public const float DefaultYaw = 20f;

        public const float DefaultPitch = 30f;

        public const float DefaultBrightness = 0.8f;

        public const float DefaultAmbient = 0.4f;

        public static SceneLighting Default { get; } = new(
            [SceneLight.FromAngles(DefaultYaw, DefaultPitch, new Vector3(DefaultBrightness))],
            new Vector3(DefaultAmbient),
            SceneFog.None);

        public IReadOnlyList<SceneLight> Lights { get; }

        public Vector3 Ambient { get; }

        public SceneFog Fog { get; }

        public SceneLighting(IReadOnlyList<SceneLight> lights, Vector3 ambient, SceneFog fog)
        {
            Lights = lights.Count > MaxLights ? [.. lights.Take(MaxLights)] : lights;
            Ambient = ambient;
            Fog = fog;
        }

        public bool NearlyEquals(SceneLighting other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (Lights.Count != other.Lights.Count || Ambient != other.Ambient || Fog != other.Fog)
                return false;

            for (var i = 0; i < Lights.Count; i++)
            {
                if (Lights[i] != other.Lights[i])
                    return false;
            }

            return true;
        }
    }
}
