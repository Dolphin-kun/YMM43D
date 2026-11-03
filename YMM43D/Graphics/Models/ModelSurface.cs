using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public sealed record ModelSurface
    {
        public static ModelSurface Default { get; } = new();

        public int BumpImage { get; init; } = -1;

        public float BumpScale { get; init; } = 1f;

        public int GlowImage { get; init; } = -1;

        public Vector3 Glow { get; init; } = Vector3.Zero;

        public int FeelImage { get; init; } = -1;

        public float Metallic { get; init; }

        public float Roughness { get; init; } = 1f;

        public int ShadeImage { get; init; } = -1;

        public Vector3 Shade { get; init; } = Vector3.One;

        public Vector2 UvScale { get; init; } = Vector2.One;

        public Vector2 UvOffset { get; init; } = Vector2.Zero;

        public float UvRotation { get; init; }

        public bool HasBump => BumpImage >= 0;

        public bool HasGlow => GlowImage >= 0 || Glow != Vector3.Zero;

        public bool HasShade => ShadeImage >= 0 || Shade != Vector3.One;
    }
}
