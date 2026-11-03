using System.Numerics;

namespace YMM43D.Commons
{
    public sealed record ModelPaint
    {
        public static ModelPaint Default { get; } = new();

        public const float DefaultFeather = 0.04f;

        public float ToonSteps { get; init; }

        public float ToonShift { get; init; }

        public float ToonFeather { get; init; } = DefaultFeather;

        public Vector3 Shade { get; init; } = Vector3.One;

        public float Bump { get; init; } = 1f;

        public float Glow { get; init; } = 1f;

        public float Metallic { get; init; }

        public float Smoothness { get; init; }

        public float Mirror { get; init; }

        public Vector2 UvOffset { get; init; } = Vector2.Zero;

        public Vector2 UvScale { get; init; } = Vector2.One;

        public float UvRotation { get; init; }
    }
}
