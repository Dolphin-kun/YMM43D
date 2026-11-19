using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;

namespace YMM43D.Project.Shape
{
    internal sealed record ShapeSlot(
        string Name,
        Vector4 Color,
        string Image,
        string NormalImage,
        float NormalStrength,
        float Metallic,
        float Roughness,
        Vector3 Glow,
        float Gloss,
        Vector2 UvScale,
        Vector2 UvOffset,
        float UvRotation)
    {
        public static ShapeSlot Plain(string name, Color color) => new(name, ToVector(color), string.Empty, string.Empty, 1f, 0f, 0.5f, Vector3.Zero, 0f, Vector2.One, Vector2.Zero, 0f);

        public static ShapeSlot Of(ShapeMaterial material, in FrameContext time) => new(
            material.Name,
            ToVector(material.Color),
            material.Image,
            material.NormalImage,
            material.NormalStrength.GetFloat(time) / 100f,
            material.Metallic.GetFloat(time) / 100f,
            material.Roughness.GetFloat(time) / 100f,
            new Vector3(material.Emission.R, material.Emission.G, material.Emission.B) / 255f * (material.EmissionStrength.GetFloat(time) / 100f),
            material.Gloss.GetFloat(time) / 100f,
            new Vector2(material.ImageScaleX.GetFloat(time), material.ImageScaleY.GetFloat(time)) / 100f,
            new Vector2(material.ImageOffsetX.GetFloat(time), material.ImageOffsetY.GetFloat(time)) / 100f,
            float.DegreesToRadians(material.ImageRotation.GetFloat(time)));

        private static Vector4 ToVector(Color color) => new Vector4(color.R, color.G, color.B, color.A) / 255f;
    }
}
