using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Model
{
    internal sealed class ListPanel : DockPanel
    {
        private const double RowHeight = 25;
        private const double ButtonColumnWidth = 30;

        private readonly UniformGrid buttons = new() { Columns = 1 };

        private readonly List<Button> lowered = [];

        public ListBox List { get; }

        public ListPanel()
        {
            var sizes = EffectEditorSettings.Default.VideoEditorSize;

            SetBinding(
                HeightProperty,
                new Binding(nameof(sizes.ListHeight)) { Source = sizes, Mode = BindingMode.OneWay });

            var grip = new Thumb { Height = 4, Cursor = Cursors.SizeNS, Opacity = 0 };

            grip.DragDelta += (_, e) => sizes.ListHeight += e.VerticalChange;

            SetDock(grip, Dock.Bottom);
            Children.Add(grip);

            List = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };

            List.Loaded += (_, _) =>
            {
                var style = new Style(typeof(ListBoxItem), List.TryFindResource(typeof(ListBoxItem)) as Style);

                style.Setters.Add(new Setter(HeightProperty, RowHeight));
                style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

                List.ItemContainerStyle = style;
            };

            List.PreviewMouseWheel += OnPreviewMouseWheel;

            buttons.Width = ButtonColumnWidth;
            SetDock(buttons, Dock.Right);
            Children.Add(buttons);
            Children.Add(List);
        }

        public Button AddButton(object geometryKey, string tip, Action onClick)
        {
            var button = ColumnButton(geometryKey, tip);

            button.Click += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };

            Insert(button, false);

            return button;
        }

        public Button AddMenuButton(object geometryKey, string tip, Action<ContextMenu> fill, bool lower = false)
        {
            var button = ColumnButton(geometryKey, tip);
            var menu = new ContextMenu();

            button.Click += (_, e) =>
            {
                e.Handled = true;

                Dispatcher.BeginInvoke(
                    () =>
                    {
                        menu.Items.Clear();
                        fill(menu);
                        menu.PlacementTarget = button;
                        menu.Placement = PlacementMode.Bottom;
                        menu.IsOpen = true;
                    },
                    DispatcherPriority.Input);
            };

            Insert(button, lower);

            return button;
        }

        public static MenuItem MenuItem(string header, Action onClick, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };

            item.Click += (_, _) => onClick();

            return item;
        }

        private void Insert(Button button, bool lower)
        {
            if (lower)
            {
                lowered.Add(button);
                buttons.Children.Add(button);

                return;
            }

            buttons.Children.Insert(buttons.Children.Count - lowered.Count, button);
        }

        public static FrameworkElement Icon(object geometryKey, double size = 14)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Stretch = Stretch.Uniform,
                MaxWidth = size,
                MaxHeight = size,
            };

            icon.SetResourceReference(System.Windows.Shapes.Path.DataProperty, geometryKey);

            icon.SetBinding(
                System.Windows.Shapes.Shape.FillProperty,
                new Binding(nameof(Control.Foreground))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(ButtonBase) },
                });

            return icon;
        }

        private static Button ColumnButton(object geometryKey, string tip)
        {
            var icon = Icon(geometryKey);

            var button = new Button
            {
                Content = icon,
                Padding = new Thickness(4),
                ToolTip = tip,
            };

            button.IsEnabledChanged += (_, _) => icon.Opacity = button.IsEnabled ? 1 : 0.35;

            return button;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var viewer = FindScrollViewer(List);

            var scrolls = viewer is not null
                && (e.Delta > 0 ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight);

            if (scrolls)
                return;

            e.Handled = true;

            RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = this,
            });
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is ScrollViewer viewer)
                    return viewer;

                if (FindScrollViewer(child) is { } found)
                    return found;
            }

            return null;
        }
    }
}
