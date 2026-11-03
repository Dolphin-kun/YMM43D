using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Resources.Icons;

namespace YMM43D.Project.Model
{
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class MaterialListAttribute : PropertyEditorAttribute
    {
        public MaterialListAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;

        public override FrameworkElement Create() => new MaterialListEditor();

        public override void SetBindings(
            FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        {
            if (control is MaterialListEditor editor)
                editor.Attach(item, propertyOwner, propertyInfo);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is MaterialListEditor editor)
                editor.Detach();
        }
    }

    internal sealed class MaterialListEditor : ContentControl, IPropertyEditorControl, IPropertyEditorControl2
    {
        private static readonly string[] Shown =
            [nameof(ModelMaterialSlot.Texture), nameof(ModelMaterialSlot.Blend), nameof(ModelMaterialSlot.Cutoff)];

        private readonly ListPanel panel = new();
        private readonly ContentControl values = new();
        private readonly TextBlock empty;

        private readonly List<(PropertyEditorAttribute2 Attribute, FrameworkElement Control, FrameworkElement Caption)> editors = [];

        private object? item;
        private object? target;
        private PropertyInfo? property;
        private IEditorInfo? editorInfo;

        private string? chosen;

        private ModelMaterialSlot? watched;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public MaterialListEditor()
        {
            panel.List.DisplayMemberPath = nameof(ModelMaterialSlot.Name);
            panel.List.SelectionChanged += (_, _) =>
            {
                if (panel.List.SelectedItem is ModelMaterialSlot slot)
                    chosen = slot.Name;

                ShowValues();
            };

            panel.AddMenuButton(
                GeometryKeys.DotsVertical,
                "そのほかの操作",
                menu =>
                {
                    menu.Items.Add(ListPanel.MenuItem(
                        "この材質を元に戻す", ResetSelected, panel.List.SelectedItem is ModelMaterialSlot));

                    menu.Items.Add(ListPanel.MenuItem("すべての材質を元に戻す", ResetAll, Slots.Count > 0));
                },
                lower: true);

            empty = new TextBlock
            {
                Text = "モデルを選ぶと、入っている材質が並びます。",
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 2, 2),
            };

            var stack = new StackPanel();

            stack.Children.Add(panel);
            stack.Children.Add(empty);
            stack.Children.Add(values);

            Content = stack;
        }

        public void SetFocus()
        {
        }

        public void SetEditorInfo(IEditorInfo? info)
        {
            editorInfo = info;

            foreach (var (_, control, _) in editors)
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

            if (owner is INotifyPropertyChanged watchedOwner)
                watchedOwner.PropertyChanged += OnOwnerPropertyChanged;

            Refresh();
        }

        public void Detach()
        {
            if (target is INotifyPropertyChanged watchedOwner)
                watchedOwner.PropertyChanged -= OnOwnerPropertyChanged;

            item = null;
            target = null;
            property = null;

            panel.List.ItemsSource = null;

            Clear();
        }

        private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == property?.Name)
                Refresh();
        }

        private ImmutableList<ModelMaterialSlot> Slots
            => target is not null && property?.GetValue(target) is ImmutableList<ModelMaterialSlot> list
                ? list
                : [];

        private void Refresh()
        {
            var slots = Slots;

            panel.List.ItemsSource = slots;
            panel.List.SelectedItem =
                slots.FirstOrDefault(slot => slot.Name == chosen) ?? slots.FirstOrDefault();

            empty.Visibility = slots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            ShowValues();
        }

        private void ShowValues()
        {
            Clear();

            if (panel.List.SelectedItem is not ModelMaterialSlot slot)
            {
                values.Content = null;

                return;
            }

            var grid = ModelEditorRows.Table();

            foreach (var name in Shown)
            {
                if (ModelEditorRows.Add(grid, item ?? slot, slot, name, null, editorInfo) is not { } made)
                    continue;

                if (made.Control is IPropertyEditorControl edited)
                {
                    edited.BeginEdit += OnBeginEdit;
                    edited.EndEdit += OnEndEdit;
                }

                editors.Add(made);
            }

            values.Content = grid;

            watched = slot;
            slot.PropertyChanged += OnSlotPropertyChanged;

            ShowCutoff();
        }

        private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModelMaterialSlot.Blend))
                ShowCutoff();
        }

        private void ShowCutoff()
        {
            if (watched is not { } slot || editors.Count < 3)
                return;

            var visible = slot.Blend == ModelSlotBlend.Mask ? Visibility.Visible : Visibility.Collapsed;

            editors[2].Control.Visibility = visible;
            editors[2].Caption.Visibility = visible;
        }

        private void ResetSelected()
        {
            if (panel.List.SelectedItem is not ModelMaterialSlot slot)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);
            Reset(slot);
            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private void ResetAll()
        {
            BeginEdit?.Invoke(this, EventArgs.Empty);

            foreach (var slot in Slots)
                Reset(slot);

            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private static void Reset(ModelMaterialSlot slot)
        {
            slot.Texture = string.Empty;
            slot.Blend = ModelSlotBlend.Model;
            slot.Cutoff = 0.5;
        }

        private void OnBeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void OnEndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Clear()
        {
            if (watched is not null)
            {
                watched.PropertyChanged -= OnSlotPropertyChanged;
                watched = null;
            }

            foreach (var (attribute, control, _) in editors)
            {
                if (control is IPropertyEditorControl edited)
                {
                    edited.BeginEdit -= OnBeginEdit;
                    edited.EndEdit -= OnEndEdit;
                }

                attribute.ClearBindings(control);
            }

            editors.Clear();
        }
    }
}
