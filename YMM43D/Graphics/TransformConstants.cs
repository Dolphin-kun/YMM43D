using System.Numerics;
using System.Runtime.InteropServices;

namespace YMM43D.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LightConstants
    {
        public Vector4 Vector;

        public Vector4 Color;
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
            bool unlit)
        {
            Matrix4x4.Invert(view, out var eye);

            if (!Matrix4x4.Invert(world, out var worldInverse))
                worldInverse = Matrix4x4.Identity;

            return new TransformConstants
            {
                WorldViewProjection = Matrix4x4.Transpose(world * view * projection),
                World = Matrix4x4.Transpose(world),
                WorldInverse = Matrix4x4.Transpose(worldInverse),
                CameraPosition = new Vector4(eye.Translation, 1f),
                Ambient = Vector4.Zero,
                FogColor = Vector4.Zero,
                Options = new Vector4(opacity, unlit ? 1f : 0f, 0f, 1f),
            };
        }

        public void SetLight(int index, in LightConstants light)
        {
            switch (index)
            {
                case 0: Light0 = light; break;
                case 1: Light1 = light; break;
                case 2: Light2 = light; break;
                case 3: Light3 = light; break;
            }
        }
    }
}
