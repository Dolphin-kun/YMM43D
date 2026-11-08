using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class MotionSelectorAttribute : PropertyEditorAttribute
    {
        public MotionSelectorAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;

        public override FrameworkElement Create() => new MotionSelector();

        public override void SetBindings(
            FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        {
            if (control is MotionSelector editor)
                editor.Attach(propertyOwner, propertyInfo);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is MotionSelector editor)
                editor.Detach();
        }
    }

    internal sealed class MotionSelector : ContentControl, IPropertyEditorControl
    {
        private const string None = "（動かさない）";

        private readonly ComboBox list = new();

        private object? target;
        private PropertyInfo? property;
        private bool filling;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public MotionSelector()
        {
            Content = list;

            list.SelectionChanged += (_, _) =>
            {
                if (filling || target is null || property is null)
                    return;

                BeginEdit?.Invoke(this, EventArgs.Empty);
                property.SetValue(target, list.SelectedIndex <= 0 ? string.Empty : (string)list.SelectedItem);
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
            if (e.PropertyName == property?.Name || e.PropertyName == nameof(Model3DParameter.File))
                Fill();
        }

        private void Fill()
        {
            filling = true;

            var names = (target as Model3DParameter)?.Model?.Animations.Select(one => one.Name) ?? [];
            var chosen = property?.GetValue(target) as string ?? string.Empty;

            list.ItemsSource = new[] { None }.Concat(names).ToArray();
            list.SelectedIndex = Math.Max(0, Array.IndexOf((string[])list.ItemsSource, chosen));

            filling = false;
        }
    }
}
