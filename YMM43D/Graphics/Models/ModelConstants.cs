using System.Numerics;
using System.Runtime.InteropServices;
using YMM43D.Commons;

namespace YMM43D.Graphics.Models
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ModelConstants
    {
        public Vector4 Uv;

        public Vector4 Bump;

        public Vector4 Glow;

        public Vector4 Shade;

        public Vector4 Feel;

        public Vector4 Mirror;

        public Vector4 Tune;

        public readonly record struct Loaded(bool Bump, bool Glow, bool Feel, bool Shade, bool Mirror);

        public static ModelConstants Create(
            ModelSurface surface,
            ModelPaint paint,
            in Loaded loaded,
            float mirrorLevels,
            float mirrorAmbient)
        {
            var scale = surface.UvScale * paint.UvScale;
            var offset = surface.UvOffset + paint.UvOffset;

            return new ModelConstants
            {
                Uv = new Vector4(scale.X, scale.Y, offset.X, offset.Y),
                Bump = new Vector4(
                    surface.UvRotation + paint.UvRotation,
                    surface.BumpScale * paint.Bump,
                    loaded.Bump ? 1f : 0f,
                    0f),
                Glow = new Vector4(surface.Glow * paint.Glow, loaded.Glow ? 1f : 0f),
                Shade = new Vector4(surface.Shade * paint.Shade, loaded.Shade ? 1f : 0f),
                Feel = new Vector4(surface.Metallic, surface.Roughness, loaded.Feel ? 1f : 0f, 0f),
                Mirror = new Vector4(
                    loaded.Mirror ? paint.Mirror : 0f,
                    mirrorLevels,
                    mirrorAmbient,
                    loaded.Mirror ? 1f : 0f),
                Tune = new Vector4(paint.Metallic, paint.Smoothness, 0f, 0f),
            };
        }
    }
}
