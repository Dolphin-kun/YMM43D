using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using YMM43D.Graphics.Models;
using YMM43D.Project.Tachie;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class ExpressionSelectorAttribute : PropertyEditorAttribute
    {
        public ExpressionSelectorAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;

        public override FrameworkElement Create() => new ExpressionSelector();

        public override void SetBindings(
            FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        {
            if (control is ExpressionSelector editor)
                editor.Attach(propertyOwner, propertyInfo);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is ExpressionSelector editor)
                editor.Detach();
        }
    }

    internal sealed class ExpressionSelector : ContentControl, IPropertyEditorControl
    {
        private const string None = "なし";

        private readonly ComboBox list = new();

        private object? target;
        private PropertyInfo? property;
        private bool filling;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ExpressionSelector()
        {
            Content = list;

            list.VerticalContentAlignment = VerticalAlignment.Center;
            list.ItemContainerStyle = new Style(typeof(ComboBoxItem))
            {
                Setters = { new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center) },
            };


            list.SelectionChanged += (_, _) =>
            {
                if (filling || target is null || property is null || list.SelectedItem is not string chosen)
                    return;

                BeginEdit?.Invoke(this, EventArgs.Empty);
                property.SetValue(target, chosen == None ? string.Empty : chosen);
                EndEdit?.Invoke(this, EventArgs.Empty);
            };
        }

        public void SetFocus()
        {
        }

        public void Attach(object owner, PropertyInfo info)
        {
            Detach();

            target = owner;
            property = info;

            if (owner is INotifyPropertyChanged watched)
                watched.PropertyChanged += OnOwnerPropertyChanged;

            Fill();
        }

        public void Detach()
        {
            if (target is INotifyPropertyChanged watched)
                watched.PropertyChanged -= OnOwnerPropertyChanged;

            target = null;
            property = null;
            list.ItemsSource = null;
        }

        private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == property?.Name || e.PropertyName == nameof(Model3DCharacterParameter.File))
                Fill();
        }

        private void Fill()
        {
            filling = true;

            var chosen = property?.GetValue(target) as string ?? string.Empty;
            var names = new List<string> { None };

            foreach (var name in Names(target))
            {
                if (name.Length > 0 && !names.Contains(name))
                    names.Add(name);
            }

            if (chosen.Length > 0 && !names.Contains(chosen))
                names.Add(chosen);

            list.ItemsSource = names;
            list.SelectedIndex = chosen.Length == 0 ? 0 : names.IndexOf(chosen);

            filling = false;
        }

        private static IEnumerable<string> Names(object? owner)
        {
            if (owner is Model3DCharacterParameter character)
                return ModelLibrary.Find(character.File)?.Expressions.Select(one => one.Name) ?? [];

            if (owner is Model3DFaceParameter)
                return ModelLibrary.Loaded.SelectMany(one => one.Expressions.Select(two => two.Name));

            return [];
        }
    }
}
