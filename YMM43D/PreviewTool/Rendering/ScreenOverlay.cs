using System.Numerics;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed record ScreenOverlay
    {
        public ScreenRegion? Region { get; init; }

        public Vector2? GuideFrom { get; init; }

        public Vector2? GuideTo { get; init; }

        public Vector3 AxisOrigin { get; init; }

        public IReadOnlyList<(Vector3 Direction, int Axis)> Axes { get; init; } = [];

        public Vector3? Cursor { get; init; }

        public IReadOnlyList<IReadOnlyList<Vector2>> Lines { get; init; } = [];

        public IReadOnlyList<Vector2> Marks { get; init; } = [];
    }
}
