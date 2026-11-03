using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    internal static class ModelEditorRows
    {
        public const double LabelWidth = 80;

        private static readonly PropertiesCache cache = new();

        public static Grid Table()
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 6) };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = LabelWidth });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            return grid;
        }

        public static (PropertyEditorAttribute2 Attribute, FrameworkElement Control, FrameworkElement Caption)? Add(
            Grid grid,
            object item,
            object owner,
            string property,
            string? label,
            IEditorInfo? editorInfo)
        {
            if (owner.GetType().GetProperty(property) is not { } info
                || cache.GetCustomAttribute<PropertyEditorAttribute2>(info) is not { } attribute)
            {
                return null;
            }

            var control = attribute.Create();

            attribute.SetBindings(control, [new ItemProperty(item, owner, info, cache)]);

            if (control is IPropertyEditorControl2 told && editorInfo is { } known)
                told.SetEditorInfo(known);

            var caption = new TextBlock
            {
                Text = label ?? cache.GetCustomAttribute<DisplayAttribute>(info)?.Name ?? property,
                VerticalAlignment = VerticalAlignment.Top,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 4, 8, 0),
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var row = grid.RowDefinitions.Count - 1;

            Grid.SetRow(caption, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);

            grid.Children.Add(caption);
            grid.Children.Add(control);

            return (attribute, control, caption);
        }
    }
}
