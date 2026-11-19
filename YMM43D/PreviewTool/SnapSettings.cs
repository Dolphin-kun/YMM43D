using YMM43D.Commons;

namespace YMM43D.PreviewTool
{
    internal readonly record struct SnapSettings(bool IsEnabled, SnapTargets Targets, SnapBase Base, SnapGrid Grid)
    {
        public bool SnapsToElements => IsEnabled && (Targets & (SnapTargets.Vertex | SnapTargets.Edge | SnapTargets.Face)) != 0;

        public bool SnapsToIncrement => IsEnabled && (Targets & SnapTargets.Increment) != 0;
    }
}
