using System.Windows.Media;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    public sealed class ShapeMaterial : Animatable
    {
        private const double Limit = 100000;

        public string Name { get; set => Set(ref field, value ?? string.Empty); } = "材質";

        public Color Color { get; set => Set(ref field, value); } = Colors.White;

        public string Image { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        public Animation ImageScaleX { get; } = new(100, -Limit, Limit);

        public Animation ImageScaleY { get; } = new(100, -Limit, Limit);

        public Animation ImageOffsetX { get; } = new(0, -Limit, Limit);

        public Animation ImageOffsetY { get; } = new(0, -Limit, Limit);

        public Animation ImageRotation { get; } = new(0, -Limit, Limit);

        public Animation Gloss { get; } = new(0, 0, 1000);

        public Animation GlossSharpness { get; } = new(50, 0, 100);

        public Animation Metallic { get; } = new(0, 0, 100);

        public Animation Roughness { get; } = new(50, 0, 100);

        public Color Emission { get; set => Set(ref field, value); } = Colors.Black;

        public Animation EmissionStrength { get; } = new(100, 0, Limit);

        public string NormalImage { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        public Animation NormalStrength { get; } = new(100, 0, Limit);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [ImageScaleX, ImageScaleY, ImageOffsetX, ImageOffsetY, ImageRotation, Gloss, GlossSharpness, Metallic, Roughness, EmissionStrength, NormalStrength];
    }
}
