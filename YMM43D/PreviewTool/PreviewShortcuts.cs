using System.Text;
using System.Windows.Input;

namespace YMM43D.PreviewTool
{
    public enum PreviewAction
    {
        ViewFront,
        ViewBack,
        ViewRight,
        ViewLeft,
        ViewTop,
        ViewBottom,
        ToggleOrthographic,
        ViewCamera,
        DriveSceneCamera,
        FocusSelected,
        ViewAll,
        LevelRoll,
        SelectAll,
        ClearSelection,
        ToggleWireframe,
        ToggleGrid,
        ToggleSnap,
        ToggleLocalSpace,
        ModeSelect,
        ModeLoopCut,
        ModeInflate,
        ModeMove,
        ModeRotate,
        ModeScale,
        AddKeyFrame,
        ToggleAutoKeyFrame,
        ToggleEditMode,
        EditVertices,
        EditEdges,
        EditFaces,
        CutLoop,
        ResetMesh,
        Extrude,
        DeleteMesh,
        MakeFace,
        MergeMesh,
        Subdivide,
        ToggleSlide,
        SelectLinked,
        InvertSelection,
        GrowSelection,
        ShrinkSelection,
    }

    public enum PreviewContext
    {
        Any,

        Object,

        Edit,
    }

    public readonly record struct PreviewShortcut(
        PreviewAction Action,
        Key Key,
        ModifierKeys Modifiers,
        bool IsPrimary = true,
        PreviewContext Context = PreviewContext.Any);

    public static class PreviewShortcuts
    {
        private const ModifierKeys None = ModifierKeys.None;
        private const ModifierKeys Ctrl = ModifierKeys.Control;
        private const ModifierKeys Shift = ModifierKeys.Shift;
        private const ModifierKeys Alt = ModifierKeys.Alt;

        public static IReadOnlyList<PreviewShortcut> All { get; } =
        [
            new(PreviewAction.ViewFront, Key.NumPad1, None),
            new(PreviewAction.ViewFront, Key.D1, None, false, PreviewContext.Object),
            new(PreviewAction.ViewBack, Key.NumPad1, Ctrl),
            new(PreviewAction.ViewBack, Key.D1, Ctrl, false),
            new(PreviewAction.ViewRight, Key.NumPad3, None),
            new(PreviewAction.ViewRight, Key.D3, None, false, PreviewContext.Object),
            new(PreviewAction.ViewLeft, Key.NumPad3, Ctrl),
            new(PreviewAction.ViewLeft, Key.D3, Ctrl, false),
            new(PreviewAction.ViewTop, Key.NumPad7, None),
            new(PreviewAction.ViewTop, Key.D7, None, false),
            new(PreviewAction.ViewBottom, Key.NumPad7, Ctrl),
            new(PreviewAction.ViewBottom, Key.D7, Ctrl, false),

            new(PreviewAction.ToggleOrthographic, Key.NumPad5, None),
            new(PreviewAction.ToggleOrthographic, Key.D5, None, false),

            new(PreviewAction.ViewCamera, Key.NumPad0, None),
            new(PreviewAction.ViewCamera, Key.D0, None, false),
            new(PreviewAction.DriveSceneCamera, Key.NumPad0, Shift),
            new(PreviewAction.DriveSceneCamera, Key.D0, Shift, false),

            new(PreviewAction.FocusSelected, Key.Decimal, None),
            new(PreviewAction.FocusSelected, Key.OemPeriod, None, false),
            new(PreviewAction.ViewAll, Key.Home, None),
            new(PreviewAction.LevelRoll, Key.R, Alt),

            new(PreviewAction.SelectAll, Key.A, None),
            new(PreviewAction.ClearSelection, Key.A, Alt),
            new(PreviewAction.ClearSelection, Key.Escape, None, false),

            new(PreviewAction.ToggleWireframe, Key.Z, Shift),
            new(PreviewAction.ToggleGrid, Key.Z, Shift | Alt),
            new(PreviewAction.ToggleSnap, Key.Tab, Shift),
            new(PreviewAction.ToggleLocalSpace, Key.OemComma, None),

            new(PreviewAction.ModeSelect, Key.Q, None),
            new(PreviewAction.ModeLoopCut, Key.C, None, true, PreviewContext.Edit),
            new(PreviewAction.ModeInflate, Key.S, Alt, true, PreviewContext.Edit),
            new(PreviewAction.ModeMove, Key.G, None),
            new(PreviewAction.ModeRotate, Key.R, None),
            new(PreviewAction.ModeScale, Key.S, None),

            new(PreviewAction.AddKeyFrame, Key.I, None),
            new(PreviewAction.ToggleAutoKeyFrame, Key.K, None),

            new(PreviewAction.ToggleEditMode, Key.Tab, None),
            new(PreviewAction.EditVertices, Key.D1, None, true, PreviewContext.Edit),
            new(PreviewAction.EditEdges, Key.D2, None, true, PreviewContext.Edit),
            new(PreviewAction.EditFaces, Key.D3, None, true, PreviewContext.Edit),
            new(PreviewAction.CutLoop, Key.R, Ctrl, true, PreviewContext.Edit),

            new(PreviewAction.Extrude, Key.E, None, true, PreviewContext.Edit),
            new(PreviewAction.DeleteMesh, Key.X, None, true, PreviewContext.Edit),
            new(PreviewAction.DeleteMesh, Key.Delete, None, false, PreviewContext.Edit),
            new(PreviewAction.MakeFace, Key.F, None, true, PreviewContext.Edit),
            new(PreviewAction.MergeMesh, Key.M, None, true, PreviewContext.Edit),
            new(PreviewAction.ToggleSlide, Key.G, Shift, true, PreviewContext.Edit),
            new(PreviewAction.SelectLinked, Key.L, None, true, PreviewContext.Edit),
            new(PreviewAction.InvertSelection, Key.I, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.GrowSelection, Key.Add, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.GrowSelection, Key.OemPlus, Ctrl, false, PreviewContext.Edit),
            new(PreviewAction.ShrinkSelection, Key.Subtract, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.ShrinkSelection, Key.OemMinus, Ctrl, false, PreviewContext.Edit),
        ];

        public static PreviewAction? Find(Key key, ModifierKeys modifiers)
            => Find(key, modifiers, false);

        public static PreviewAction? Find(Key key, ModifierKeys modifiers, bool editing)
        {
            var wanted = editing ? PreviewContext.Edit : PreviewContext.Object;
            PreviewAction? loose = null;

            foreach (var shortcut in All)
            {
                if (shortcut.Key != key || shortcut.Modifiers != modifiers)
                    continue;

                if (shortcut.Context == wanted)
                    return shortcut.Action;

                if (shortcut.Context == PreviewContext.Any)
                    loose ??= shortcut.Action;
            }

            return loose;
        }

        public static string TextOf(PreviewAction action)
        {
            foreach (var shortcut in All)
            {
                if (shortcut.Action == action && shortcut.IsPrimary)
                    return Describe(shortcut);
            }

            return string.Empty;
        }

        private static string Describe(in PreviewShortcut shortcut)
        {
            var text = new StringBuilder();

            if ((shortcut.Modifiers & Ctrl) != 0)
                text.Append("Ctrl+");

            if ((shortcut.Modifiers & Shift) != 0)
                text.Append("Shift+");

            if ((shortcut.Modifiers & Alt) != 0)
                text.Append("Alt+");

            return text.Append(NameOf(shortcut.Key)).ToString();
        }

        private static string NameOf(Key key) => key switch
        {
            Key.NumPad0 => "テンキー 0",
            Key.NumPad1 => "テンキー 1",
            Key.NumPad3 => "テンキー 3",
            Key.NumPad5 => "テンキー 5",
            Key.NumPad7 => "テンキー 7",
            Key.Decimal => "テンキー .",
            Key.Add => "テンキー +",
            Key.Subtract => "テンキー -",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.Escape => "Esc",
            Key.D0 or Key.D1 or Key.D2 or Key.D3 or Key.D5 or Key.D7 => ((int)key - (int)Key.D0).ToString(),
            _ => key.ToString(),
        };
    }

    public sealed class PreviewKeyText
    {
        public string this[string action]
            => Enum.TryParse<PreviewAction>(action, out var parsed)
                ? PreviewShortcuts.TextOf(parsed)
                : string.Empty;
    }
}
