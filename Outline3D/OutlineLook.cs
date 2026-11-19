using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;

namespace Outline3D
{
    internal readonly record struct OutlineLook(
        float Thickness,
        float Blur,
        float Sides,
        float Smoothness,
        bool IsOutlineOnly,
        bool IsAngular,
        Vector2 Offset,
        float Zoom,
        float Rotation,
        Vector4 Color,
        ID3D11ShaderResourceView? Brush,
        Vector2 BrushSize);

    internal delegate void SourceDraw(in Render3DContext render, DrawContext3D item);
}
