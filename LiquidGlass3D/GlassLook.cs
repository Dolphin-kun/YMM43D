using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace LiquidGlass3D
{
    internal interface IGlassLook
    {
        Animation RefractiveIndex { get; }

        Animation Distance { get; }

        Animation Frost { get; }

        Animation Dispersion { get; }

        Color TintColor { get; }

        Animation TintAmount { get; }

        Animation Reflection { get; }

        Animation Gloss { get; }

        Animation GlossSharpness { get; }
    }

    internal readonly record struct GlassLook(float RefractiveIndex, float Distance, float Frost, float Dispersion, Vector3 Tint, float TintAmount, float Reflection, SurfaceGloss Gloss)
    {
        public static GlassLook From(IGlassLook source, in FrameContext time) => new(
            Math.Clamp(source.RefractiveIndex.GetFloat(time), 1f, 4f),
            MathF.Max(source.Distance.GetFloat(time), 0f),
            Math.Clamp(source.Frost.GetFloat(time) / 100f, 0f, 1f),
            MathF.Max(source.Dispersion.GetFloat(time), 0f) / 100f,
            source.TintColor.ToVector3(),
            Math.Clamp(source.TintAmount.GetFloat(time) / 100f, 0f, 1f),
            Math.Clamp(source.Reflection.GetFloat(time) / 100f, 0f, 1f),
            SurfaceGloss.FromPercent(source.Gloss.GetFloat(time), source.GlossSharpness.GetFloat(time)));

        public GlassConstants ToConstants(Vector3 size, float cornerRadius, Vector3 camera, float shape, CaptureTarget? captured, float normalStep) => new()
        {
            Size = new Vector4(size, cornerRadius),
            Camera = new Vector4(camera, shape),
            Optics = new Vector4(RefractiveIndex, Distance, Frost, Dispersion),
            Tint = new Vector4(Tint, TintAmount),
            Surface = new Vector4(Reflection, captured is { } capture ? capture.MipLevels : 0f, captured is null ? 0f : 1f, normalStep),
        };
    }
}
