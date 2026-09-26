using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    // 「表情」の一覧を、名前とスライダーの 1 行ずつで出す。
    internal sealed class ExpressionListEditor : ContentControl, IPropertyEditorControl, IPropertyEditorControl2
    {
        private readonly StackPanel panel = new();
        private readonly List<(PropertyEditorAttribute2 Attribute, FrameworkElement Control)> editors = [];

        private object? item;
        private object? target;
        private PropertyInfo? property;
        private IEditorInfo? editorInfo;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ExpressionListEditor() => Content = panel;

        public void SetFocus()
        {
        }

        public void SetEditorInfo(IEditorInfo? info)
        {
            editorInfo = info;

            foreach (var (_, control) in editors)
            {
                if (control is IPropertyEditorControl2 told)
                    told.SetEditorInfo(info);
            }
        }

        public void Attach(object editedItem, object owner, PropertyInfo info)
        {
            Detach();

            item = editedItem;
            target = owner;
            property = info;

            if (owner is INotifyPropertyChanged watched)
                watched.PropertyChanged += OnOwnerPropertyChanged;

            Rebuild();
        }

        public void Detach()
        {
            if (target is INotifyPropertyChanged watched)
                watched.PropertyChanged -= OnOwnerPropertyChanged;

            item = null;
            target = null;
            property = null;

            Clear();
        }

        private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == property?.Name)
                Rebuild();
        }

        private ImmutableList<ModelExpressionValue> Values
            => target is not null && property?.GetValue(target) is ImmutableList<ModelExpressionValue> list
                ? list
                : [];

        private void Rebuild()
        {
            Clear();

            var values = Values;

            if (values.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "このモデルには表情が入っていません。",
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2),
                });

                return;
            }

            var grid = ModelEditorRows.Table();

            foreach (var value in values)
            {
                if (ModelEditorRows.Add(grid, item ?? value, value, nameof(ModelExpressionValue.Weight), value.Name, editorInfo)
                    is not { } made)
                {
                    continue;
                }

                if (made.Control is IPropertyEditorControl edited)
                {
                    edited.BeginEdit += OnBeginEdit;
                    edited.EndEdit += OnEndEdit;
                }

                editors.Add(made);
            }

            panel.Children.Add(grid);
        }

        private void OnBeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void OnEndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Clear()
        {
            foreach (var (attribute, control) in editors)
            {
                if (control is IPropertyEditorControl edited)
                {
                    edited.BeginEdit -= OnBeginEdit;
                    edited.EndEdit -= OnEndEdit;
                }

                attribute.ClearBindings(control);
            }

            editors.Clear();
            panel.Children.Clear();
        }
    }
}
