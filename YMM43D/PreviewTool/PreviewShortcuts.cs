using System.Text;
using System.Windows.Input;

namespace YMM43D.PreviewTool
{
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
            new(PreviewAction.ToggleSidebar, Key.N, None),
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
            new(PreviewAction.ExtrudeMenu, Key.E, Alt, true, PreviewContext.Edit),
            new(PreviewAction.Crease, Key.E, Shift, true, PreviewContext.Edit),
            new(PreviewAction.DeleteMenu, Key.X, None, true, PreviewContext.Edit),
            new(PreviewAction.DeleteMenu, Key.Delete, None, false, PreviewContext.Edit),
            new(PreviewAction.MergeMenu, Key.M, None, true, PreviewContext.Edit),
            new(PreviewAction.VertexMenu, Key.V, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.EdgeMenu, Key.E, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.FaceMenu, Key.F, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.NormalMenu, Key.N, Alt, true, PreviewContext.Edit),
            new(PreviewAction.UvMenu, Key.U, None, true, PreviewContext.Edit),
            new(PreviewAction.ToggleUvView, Key.U, Shift, true, PreviewContext.Edit),
            new(PreviewAction.AddMenu, Key.A, Shift, true, PreviewContext.Edit),
            new(PreviewAction.Fill, Key.F, None, true, PreviewContext.Edit),
            new(PreviewAction.Knife, Key.K, None, true, PreviewContext.Edit),
            new(PreviewAction.Duplicate, Key.D, Shift, true, PreviewContext.Edit),
            new(PreviewAction.Separate, Key.P, None, true, PreviewContext.Edit),
            new(PreviewAction.Connect, Key.J, None, true, PreviewContext.Edit),
            new(PreviewAction.Triangulate, Key.T, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.TrianglesToQuads, Key.J, Alt, true, PreviewContext.Edit),
            new(PreviewAction.NormalsOutside, Key.N, Shift, true, PreviewContext.Edit),
            new(PreviewAction.NormalsInside, Key.N, Ctrl | Shift, true, PreviewContext.Edit),
            new(PreviewAction.Join, Key.J, Ctrl, true, PreviewContext.Object),
            new(PreviewAction.ToggleSlide, Key.G, Shift, true, PreviewContext.Edit),
            new(PreviewAction.SelectLinked, Key.L, None, true, PreviewContext.Edit),
            new(PreviewAction.InvertSelection, Key.I, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.GrowSelection, Key.Add, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.GrowSelection, Key.OemPlus, Ctrl, false, PreviewContext.Edit),
            new(PreviewAction.ShrinkSelection, Key.Subtract, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.ShrinkSelection, Key.OemMinus, Ctrl, false, PreviewContext.Edit),

            new(PreviewAction.TransformMove, Key.G, None, true, PreviewContext.Edit),
            new(PreviewAction.TransformRotate, Key.R, None, true, PreviewContext.Edit),
            new(PreviewAction.TransformScale, Key.S, None, true, PreviewContext.Edit),
            new(PreviewAction.Inset, Key.I, None, true, PreviewContext.Edit),
            new(PreviewAction.Bevel, Key.B, Ctrl, true, PreviewContext.Edit),
            new(PreviewAction.BoxSelect, Key.B, None, true, PreviewContext.Edit),
            new(PreviewAction.CycleSelectShape, Key.W, None, true, PreviewContext.Edit),
            new(PreviewAction.ToggleXRay, Key.Z, Alt, true, PreviewContext.Edit),
            new(PreviewAction.HideSelected, Key.H, None, true, PreviewContext.Edit),
            new(PreviewAction.HideOthers, Key.H, Shift, true, PreviewContext.Edit),
            new(PreviewAction.RevealHidden, Key.H, Alt, true, PreviewContext.Edit),
            new(PreviewAction.ToggleProportional, Key.O, None, true, PreviewContext.Edit),
            new(PreviewAction.ToggleConnected, Key.O, Alt, true, PreviewContext.Edit),
            new(PreviewAction.CycleFalloff, Key.O, Shift, true, PreviewContext.Edit),
            new(PreviewAction.CyclePivot, Key.OemPeriod, None, true, PreviewContext.Edit),
            new(PreviewAction.CursorToSelected, Key.S, Shift, true, PreviewContext.Edit),
            new(PreviewAction.CursorToOrigin, Key.C, Shift, true, PreviewContext.Edit),
        ];

        public static PreviewAction? Find(Key key, ModifierKeys modifiers) => Find(key, modifiers, false);

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
}
