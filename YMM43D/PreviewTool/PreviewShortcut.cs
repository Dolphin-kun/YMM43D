using System.Windows.Input;

namespace YMM43D.PreviewTool
{
    public readonly record struct PreviewShortcut(
        PreviewAction Action,
        Key Key,
        ModifierKeys Modifiers,
        bool IsPrimary = true,
        PreviewContext Context = PreviewContext.Any);
}
