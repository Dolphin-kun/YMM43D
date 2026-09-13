using System.ComponentModel.DataAnnotations;
using System.Numerics;
using YMM43D.Graphics;

namespace YMM43D.Commons
{
    public static class SceneConstants
    {
        public static TransformConstants Create(
            in Matrix4x4 world,
            in Matrix4x4 view,
            in Matrix4x4 projection,
            float opacity,
            SceneLighting? lighting = null,
            bool unlit = false,
            float alphaCutoff = 0f)
        {
            var scene = lighting ?? SceneLighting.Default;
            var fog = scene.Fog;

            var constants = TransformConstants.Create(world, view, projection, opacity, unlit, alphaCutoff);

            constants.Ambient = new Vector4(scene.Ambient, 1f);
            constants.FogColor = new Vector4(fog.Color, fog.IsEnabled ? fog.Density : 0f);
            constants.Options.Z = fog.Start;
            constants.Options.W = fog.End;
            constants.Surface.Y = scene.Lights.Count;

            return constants;
        }

        public static LightConstants ToConstants(SceneLight light) => new()
        {
            Vector = new Vector4(light.Vector, ToShaderKind(light.Kind)),
            Color = new Vector4(light.Color, light.Reach),
            Cone = new Vector4(light.Axis, Cosine(light.OuterAngle)),
            Edge = new Vector4(
                Cosine(light.InnerAngle),
                light.Shadow.Slice,
                light.Shadow.Strength,
                light.Shadow.Texel),
            Shadow = Matrix4x4.Transpose(light.Shadow.Matrix),
        };

        private static float ToShaderKind(LightKind kind) => kind switch
        {
            LightKind.Point => 1f,
            LightKind.Spot => 2f,
            _ => 0f,
        };

        private static float Cosine(float degrees) => MathF.Cos(float.DegreesToRadians(degrees));
    }

    public enum LightKind
    {
        [Display(Name = "平行光", Description = "太陽のように、どこでも同じ向きから当たります")]
        Directional,

        [Display(Name = "点光源", Description = "電球のように、置いた場所から周りへ広がります")]
        Point,

        [Display(Name = "スポットライト", Description = "舞台の照明のように、円錐の形に絞って照らします")]
        Spot,
    }

    public readonly record struct ShadowPlacement(
        float Strength,
        int Slice = -1,
        float Texel = 0f,
        Matrix4x4 Matrix = default)
    {
        public static ShadowPlacement None => default;

        public bool IsWanted => Strength > 0f;

        public bool IsPlaced => Slice >= 0;
    }

    public readonly record struct SceneLight(
        LightKind Kind,
        Vector3 Vector,
        Vector3 Color,
        float Reach,
        Vector3 Axis = default,
        float InnerAngle = 0f,
        float OuterAngle = 0f,
        ShadowPlacement Shadow = default)
    {
        public bool CanCastShadow => Kind is LightKind.Directional or LightKind.Spot;

        public SceneLight WithShadow(float strength)
            => this with { Shadow = new ShadowPlacement(Math.Clamp(strength, 0f, 1f)) };

        public SceneLight PlacedAt(int slice, float texel, in Matrix4x4 matrix)
            => this with { Shadow = Shadow with { Slice = slice, Texel = texel, Matrix = matrix } };

        public static SceneLight Directional(Vector3 direction, Vector3 color)
            => new(LightKind.Directional, Normalize(direction), color, 0f);

        public static SceneLight FromAngles(float yaw, float pitch, Vector3 color)
            => Directional(ToDirection(yaw, pitch), color);

        public static Vector3 ToDirection(float yaw, float pitch)
        {
            var y = float.DegreesToRadians(yaw);
            var p = float.DegreesToRadians(Math.Clamp(pitch, -90f, 90f));

            var flat = MathF.Cos(p);

            return new Vector3(-flat * MathF.Sin(y), MathF.Sin(p), flat * MathF.Cos(y));
        }

        public static (float Yaw, float Pitch) ToAngles(in Vector3 direction)
        {
            var flat = new Vector2(direction.X, direction.Z);

            if (flat.LengthSquared() < 1e-12f)
                return (0f, direction.Y >= 0f ? 90f : -90f);

            return (
                float.RadiansToDegrees(MathF.Atan2(-direction.X, direction.Z)),
                float.RadiansToDegrees(MathF.Atan2(direction.Y, flat.Length())));
        }

        public static SceneLight Point(Vector3 position, Vector3 color, float reach)
            => new(LightKind.Point, position, color, MathF.Max(reach, 0.01f));

        public const float MinSpread = 1f;

        public const float MaxSpread = 89f;

        public static SceneLight Spot(
            Vector3 position, Vector3 shines, Vector3 color, float reach, float spread, float blur)
        {
            var outer = Math.Clamp(spread, MinSpread, MaxSpread);

            return new SceneLight(
                LightKind.Spot,
                position,
                color,
                MathF.Max(reach, 0.01f),
                Normalize(shines),
                outer * (1f - Math.Clamp(blur, 0f, 1f)),
                outer);
        }

        private static Vector3 Normalize(in Vector3 value)
            => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : new Vector3(0f, 0f, 1f);
    }

    public readonly record struct SceneFog(Vector3 Color, float Density, float Start, float End)
    {
        public static SceneFog None => new(Vector3.Zero, 0f, 0f, 1f);

        public bool IsEnabled => Density > 0f && End > Start;
    }

    public sealed class SceneLighting(IReadOnlyList<SceneLight> lights, Vector3 ambient, SceneFog fog)
    {
        public const float DefaultYaw = 20f;

        public const float DefaultPitch = 30f;

        public const float DefaultBrightness = 0.8f;

        public const float DefaultAmbient = 0.4f;

        public static SceneLighting Default { get; } = new(
            [SceneLight.FromAngles(DefaultYaw, DefaultPitch, new Vector3(DefaultBrightness))],
            new Vector3(DefaultAmbient),
            SceneFog.None);

        public IReadOnlyList<SceneLight> Lights { get; } = lights;

        public Vector3 Ambient { get; } = ambient;

        public SceneFog Fog { get; } = fog;

        public IReadOnlyList<LightConstants> LightBuffer
            => lightBuffer ??= [.. Lights.Select(SceneConstants.ToConstants)];
        private LightConstants[]? lightBuffer;

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
