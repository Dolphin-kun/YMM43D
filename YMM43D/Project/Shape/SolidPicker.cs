using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    public readonly record struct SolidEntry(SolidKind Kind, string Name, string Tip);

    public readonly record struct SolidGroup(string Name, SolidEntry[] Entries);

    public static class SolidGroups
    {
        public static SolidGroup[] All { get; } =
        [
            new("メッシュ",
            [
                new(SolidKind.Plane, "平面", "1枚の四角形"),
                new(SolidKind.Cube, "立方体", "正方形6枚。六面体ともいいます"),
                new(SolidKind.Sphere, "球", "分割の細かさで滑らかさが決まります"),
                new(SolidKind.Cylinder, "円柱", "色は 側面・上面・底面 の順です"),
                new(SolidKind.Cone, "円錐", "色は 側面・底面 の順です"),
                new(SolidKind.Torus, "ドーナツ", "太さで穴の大きさが決まります"),
            ]),

            new("正多面体",
            [
                new(SolidKind.Tetrahedron, "四面体", "正三角形4枚"),
                new(SolidKind.Octahedron, "八面体", "正三角形8枚"),
                new(SolidKind.Dodecahedron, "十二面体", "正五角形12枚"),
                new(SolidKind.Icosahedron, "二十面体", "正三角形20枚"),
            ]),
        ];

        public static string NameOf(SolidKind kind)
        {
            foreach (var group in All)
            {
                foreach (var entry in group.Entries)
                {
                    if (entry.Kind == kind)
                        return entry.Name;
                }
            }

            return kind == SolidKind.Custom ? "自由な形" : kind.ToString();
        }

        public static string PathOf(SolidKind kind)
        {
            foreach (var group in All)
            {
                foreach (var entry in group.Entries)
                {
                    if (entry.Kind == kind)
                        return $"{group.Name} / {entry.Name}";
                }
            }

            return NameOf(kind);
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class SolidPickerAttribute : PropertyEditorAttribute
    {
        public override FrameworkElement Create() => new SolidPickerEditor();

        public override void SetBindings(
            FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        {
            if (control is SolidPickerEditor editor)
                editor.Attach(propertyOwner as Shape3DParameter);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is SolidPickerEditor editor)
                editor.Attach(null);
        }
    }

    internal sealed class SolidPickerEditor : ContentControl, IPropertyEditorControl
    {
        private readonly Button button = new()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 2, 4, 2),
        };

        private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };

        private Shape3DParameter? parameter;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public SolidPickerEditor()
        {
            var arrow = new TextBlock
            {
                Text = "▼",
                FontSize = 9,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            DockPanel.SetDock(arrow, Dock.Right);

            var row = new DockPanel();

            row.Children.Add(arrow);
            row.Children.Add(label);

            button.Content = row;
            button.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Open();
            };

            button.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true;
            button.PreviewMouseRightButtonDown += (_, e) => e.Handled = true;

            Content = button;
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        public void SetFocus() => button.Focus();

        public void Attach(Shape3DParameter? owner)
        {
            if (parameter is not null)
                parameter.PropertyChanged -= OnParameterPropertyChanged;

            parameter = owner;

            if (parameter is not null)
                parameter.PropertyChanged += OnParameterPropertyChanged;

            Refresh();
        }

        private void OnParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or nameof(Shape3DParameter.Solid))
                Refresh();
        }

        private void Refresh()
            => label.Text = parameter is null ? string.Empty : SolidGroups.NameOf(parameter.Solid);

        private void Open()
        {
            if (Build() is not { } menu)
                return;

            if (button.ContextMenu is { } previous)
                previous.IsOpen = false;

            button.ContextMenu = menu;

            menu.IsOpen = true;
        }

        internal ContextMenu? Build()
        {
            if (parameter is null)
                return null;

            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom,
                MinWidth = button.ActualWidth,
            };

            foreach (var group in SolidGroups.All)
            {
                var parent = new MenuItem { Header = group.Name };

                foreach (var entry in group.Entries)
                {
                    var chosen = entry.Kind;

                    var row = new MenuItem
                    {
                        Header = entry.Name,
                        ToolTip = entry.Tip,
                        IsCheckable = true,
                        IsChecked = parameter.Solid == chosen,
                    };

                    row.Click += (_, e) =>
                    {
                        e.Handled = true;
                        Choose(chosen);
                    };

                    parent.Items.Add(row);
                }

                menu.Items.Add(parent);
            }

            return menu;
        }

        private void Choose(SolidKind kind)
        {
            if (parameter is null || parameter.Solid == kind)
            {
                Refresh();

                return;
            }

            BeginEdit?.Invoke(this, EventArgs.Empty);
            parameter.DropMesh();
            parameter.Solid = kind;
            EndEdit?.Invoke(this, EventArgs.Empty);

            Refresh();
        }
    }
}
