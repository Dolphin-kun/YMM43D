using System.Numerics;
using System.Runtime.InteropServices;
using YMM43D.Scene3D;

namespace YMM43D.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LightConstants
    {
        public Vector4 Vector;

        public Vector4 Color;

        public static LightConstants From(in SceneLight light) => new()
        {
            Vector = new Vector4(light.Vector, light.Kind == LightKind.Point ? 1f : 0f),
            Color = new Vector4(light.Color, light.Reach),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TransformConstants
    {
        public Matrix4x4 WorldViewProjection;

        public Matrix4x4 World;

        public Matrix4x4 WorldInverse;

        public Vector4 CameraPosition;

        public Vector4 Ambient;

        public Vector4 FogColor;

        public Vector4 Options;

        public LightConstants Light0;

        public LightConstants Light1;

        public LightConstants Light2;

        public LightConstants Light3;

        public static TransformConstants CreateUnlit(in Matrix4x4 worldViewProjection, float opacity) => new()
        {
            WorldViewProjection = Matrix4x4.Transpose(worldViewProjection),
            World = Matrix4x4.Identity,
            WorldInverse = Matrix4x4.Identity,
            Options = new Vector4(opacity, 1f, 0f, 1f),
        };

        public static TransformConstants Create(
            in Matrix4x4 world,
            in Matrix4x4 view,
            in Matrix4x4 projection,
            float opacity,
            SceneLighting? lighting = null,
            bool unlit = false)
        {
            var scene = lighting ?? SceneLighting.Default;
            var lights = scene.Lights;
            var fog = scene.Fog;

            Matrix4x4.Invert(view, out var eye);

            if (!Matrix4x4.Invert(world, out var worldInverse))
                worldInverse = Matrix4x4.Identity;

            var constants = new TransformConstants
            {
                WorldViewProjection = Matrix4x4.Transpose(world * view * projection),
                World = Matrix4x4.Transpose(world),
                WorldInverse = Matrix4x4.Transpose(worldInverse),
                CameraPosition = new Vector4(eye.Translation, 1f),
                Ambient = new Vector4(scene.Ambient, 1f),
                FogColor = new Vector4(fog.Color, fog.IsEnabled ? fog.Density : 0f),
                Options = new Vector4(opacity, unlit ? 1f : 0f, fog.Start, fog.End),
            };

            constants.Light0 = At(lights, 0);
            constants.Light1 = At(lights, 1);
            constants.Light2 = At(lights, 2);
            constants.Light3 = At(lights, 3);

            return constants;
        }

        private static LightConstants At(IReadOnlyList<SceneLight> lights, int index)
            => index < lights.Count
                ? LightConstants.From(lights[index])
                : new LightConstants { Vector = Vector4.Zero, Color = Vector4.Zero };
    }
}
