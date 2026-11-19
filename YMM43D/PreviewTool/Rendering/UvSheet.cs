using System.Numerics;
using YMM43D.Graphics.Models;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed record UvSheet(
        ModelImage? Image,
        Vector2 Origin,
        float Zoom,
        Vector2[] Lines,
        Vector2[] SeamLines,
        Vector2[] ChosenLines,
        Vector2[] Dots,
        Vector2[] ChosenDots,
        Vector2[][] ChosenFaces,
        (Vector2 From, Vector2 To)? Box);
}
