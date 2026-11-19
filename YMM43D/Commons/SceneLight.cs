using System.Numerics;

namespace YMM43D.Commons
{
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
        public const float MinSpread = 1f;
        public const float MaxSpread = 89f;

        public int ShadowSlices => Kind == LightKind.Point ? 6 : 1;

        public SceneLight WithShadow(float strength, float softness = 0f)
            => this with { Shadow = new ShadowPlacement(Math.Clamp(strength, 0f, 1f), Math.Clamp(softness, 0f, 1f)) };

        public SceneLight PlacedAt(int slice, float radius, in Matrix4x4 matrix)
            => this with { Shadow = Shadow with { Slice = slice, Radius = radius, Matrix = matrix } };

        public static SceneLight Directional(Vector3 direction, Vector3 color) => new(LightKind.Directional, Normalize(direction), color, 0f);

        public static SceneLight FromAngles(float yaw, float pitch, Vector3 color) => Directional(ToDirection(yaw, pitch), color);

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

            return (float.RadiansToDegrees(MathF.Atan2(-direction.X, direction.Z)), float.RadiansToDegrees(MathF.Atan2(direction.Y, flat.Length())));
        }

        public static SceneLight Point(Vector3 position, Vector3 color, float reach) => new(LightKind.Point, position, color, MathF.Max(reach, 0.01f));

        public static SceneLight Spot(Vector3 position, Vector3 shines, Vector3 color, float reach, float spread, float blur)
        {
            var outer = Math.Clamp(spread, MinSpread, MaxSpread);

            return new SceneLight(LightKind.Spot, position, color, MathF.Max(reach, 0.01f), Normalize(shines), outer * (1f - Math.Clamp(blur, 0f, 1f)), outer);
        }

        private static Vector3 Normalize(in Vector3 value) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : new Vector3(0f, 0f, 1f);
    }
}
