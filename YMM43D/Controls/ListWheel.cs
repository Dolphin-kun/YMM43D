using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace YMM43D.Controls
{
    public static class ListWheel
    {
        public static void PassOn(ListBox list, MouseWheelEventArgs e)
        {
            if (e.Handled || FindScroller(list) is { ScrollableHeight: > 0 })
                return;

            e.Handled = true;

            var outer = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = list,
            };

            (VisualTreeHelper.GetParent(list) as UIElement)?.RaiseEvent(outer);
        }

        private static ScrollViewer? FindScroller(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is ScrollViewer found)
                    return found;

                if (FindScroller(child) is { } deeper)
                    return deeper;
            }

            return null;
        }
    }
}
