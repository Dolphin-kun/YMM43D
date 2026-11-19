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
            float alphaCutoff = 0f,
            SurfaceGloss gloss = default,
            ModelPaint? paint = null)
        {
            var scene = lighting ?? SceneLighting.Default;
            var fog = scene.Fog;
            var look = paint ?? ModelPaint.Default;

            var constants = TransformConstants.Create(world, view, projection, opacity, unlit, alphaCutoff);

            constants.Ambient = new Vector4(scene.Ambient, 1f);
            constants.FogColor = new Vector4(fog.Color, fog.IsEnabled ? fog.Density : 0f);
            constants.Options.Z = fog.Start;
            constants.Options.W = fog.End;
            constants.Surface.Y = scene.Lights.Count;
            constants.Surface.Z = gloss.Strength;
            constants.Surface.W = gloss.Power;
            constants.Shading.X = look.ToonSteps;
            constants.Shading.Y = look.ToonShift;
            constants.Shading.Z = look.ToonFeather;
            constants.Ground = scene.Ground is { } ground ? new Vector4(ground, 1f) : new Vector4(scene.Ambient, 0f);

            return constants;
        }

        public static LightConstants ToConstants(SceneLight light) => new()
        {
            Vector = new Vector4(light.Vector, ToShaderKind(light.Kind)),
            Color = new Vector4(light.Color, light.Reach),
            Cone = new Vector4(light.Axis, Cosine(light.OuterAngle)),
            Edge = new Vector4(Cosine(light.InnerAngle), light.Shadow.Slice, light.Shadow.Strength, light.Shadow.Radius),
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
}
