using System.Windows.Input;

namespace YMM43D.PreviewTool
{
    public sealed record MeshMenuEntry(string Header, ICommand? Command, string Parameter, string Gesture, string? ToolTip, bool IsSeparator, IReadOnlyList<MeshMenuEntry>? Items = null);
}
