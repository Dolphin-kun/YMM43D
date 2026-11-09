using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YMM43D.Graphics.Models;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Resources.Icons;

namespace YMM43D.Project.Model
{
    internal sealed class BonePoseListEditor : ContentControl, IPropertyEditorControl, IPropertyEditorControl2
    {
        private static readonly string[] Shown =
        [
            nameof(ModelBonePose.RotationX),
            nameof(ModelBonePose.RotationY),
            nameof(ModelBonePose.RotationZ),
            nameof(ModelBonePose.Swings),
        ];

        private readonly ListPanel panel = new();
        private readonly Button remove;
        private readonly ContentControl values = new();

        private readonly List<(PropertyEditorAttribute2 Attribute, FrameworkElement Control, FrameworkElement Caption)> editors = [];

        private object? item;
        private object? target;
        private PropertyInfo? property;
        private IEditorInfo? editorInfo;

        private string? chosen;
        private string[] listed = [];

        private readonly Dictionary<string, int> order = [];
        private readonly Dictionary<string, string> parents = [];

        private readonly Dictionary<string, int> depths = [];

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public BonePoseListEditor()
        {
            var label = new FrameworkElementFactory(typeof(TextBlock));

            label.SetBinding(TextBlock.TextProperty, new Binding { Converter = new BoneLabel(DepthOf) });
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            panel.List.ItemTemplate = new DataTemplate { VisualTree = label };
            panel.List.SelectionChanged += (_, _) =>
            {
                if (panel.List.SelectedItem is ModelBonePose pose)
                    chosen = pose.Name;

                ShowValues();
            };

            panel.AddMenuButton(GeometryKeys.Plus, "ボーンを足す", FillAddMenu);
            remove = panel.AddButton(GeometryKeys.Minus, "選んでいるボーンの回転をやめる", RemoveSelected);
            panel.AddMenuButton(
                GeometryKeys.DotsVertical,
                "そのほかの操作",
                menu =>
                {
                    menu.Items.Add(ListPanel.MenuItem(
                        "このボーンを元に戻す", ResetSelected, panel.List.SelectedItem is ModelBonePose));

                    menu.Items.Add(ListPanel.MenuItem("すべてのボーンを元に戻す", ResetAll, Poses.Count > 0));
                },
                lower: true);

            var stack = new StackPanel();

            stack.Children.Add(panel);
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

            if (owner is INotifyPropertyChanged watched)
                watched.PropertyChanged += OnOwnerPropertyChanged;

            Refresh();
        }

        public void Detach()
        {
            if (target is INotifyPropertyChanged watched)
                watched.PropertyChanged -= OnOwnerPropertyChanged;

            item = null;
            target = null;
            property = null;

            panel.List.ItemsSource = null;
            listed = [];

            Clear();
        }

        private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == property?.Name)
                Refresh();
        }

        private ImmutableList<ModelBonePose> Poses
            => target is not null && property?.GetValue(target) is ImmutableList<ModelBonePose> list
                ? list
                : [];

        private ModelData? Model => (target as IBoneHost)?.Model;

        private int DepthOf(string name) => depths.TryGetValue(name, out var depth) ? depth : 0;

        private int OrderOf(string name) => order.TryGetValue(name, out var at) ? at : int.MaxValue;

        private void ReadModel()
        {
            order.Clear();
            parents.Clear();

            if (Model is not { HasBones: true } model)
                return;

            for (var i = 0; i < model.Bones.Length; i++)
            {
                var name = model.Bones[i].Name;

                if (order.ContainsKey(name))
                    continue;

                order[name] = i;

                var parent = model.Bones[i].Parent;

                if (parent >= 0 && parent < model.Bones.Length)
                    parents[name] = model.Bones[parent].Name;
            }
        }

        private void MeasureDepths(IEnumerable<ModelBonePose> poses)
        {
            depths.Clear();

            var listing = poses.Select(pose => pose.Name).ToHashSet();

            foreach (var name in listing)
            {
                var depth = 0;
                var at = name;

                for (var guard = 0; guard <= order.Count; guard++)
                {
                    if (!parents.TryGetValue(at, out var parent) || parent.Length == 0)
                        break;

                    if (listing.Contains(parent))
                        depth++;

                    at = parent;
                }

                depths[name] = depth;
            }
        }

        private void Refresh()
        {
            ReadModel();

            var poses = Poses;

            MeasureDepths(poses);

            var added = poses.Select(pose => pose.Name).Except(listed).ToArray();

            if (added.Length > 0)
                chosen = added[^1];

            listed = [.. poses.Select(pose => pose.Name)];

            panel.List.ItemsSource = poses.OrderBy(pose => OrderOf(pose.Name)).ToArray();
            panel.List.SelectedItem =
                poses.FirstOrDefault(pose => pose.Name == chosen) ?? poses.FirstOrDefault();

            ShowValues();
        }

        private void ShowValues()
        {
            Clear();

            remove.IsEnabled = panel.List.SelectedItem is ModelBonePose;

            if (panel.List.SelectedItem is not ModelBonePose pose)
            {
                values.Content = null;

                return;
            }

            var grid = ModelEditorRows.Table();

            foreach (var name in Shown)
            {
                if (ModelEditorRows.Add(grid, item ?? pose, pose, name, null, editorInfo) is not { } made)
                    continue;

                if (made.Control is IPropertyEditorControl edited)
                {
                    edited.BeginEdit += OnBeginEdit;
                    edited.EndEdit += OnEndEdit;
                }

                editors.Add(made);
            }

            values.Content = grid;
        }

        private void FillAddMenu(ContextMenu menu)
        {
            if (Model is not { HasBones: true } model)
            {
                menu.Items.Add(ListPanel.MenuItem("このモデルにはボーンが入っていません", () => { }, false));

                return;
            }

            var already = Poses.Select(pose => pose.Name).ToHashSet();

            for (var i = 0; i < model.Bones.Length; i++)
            {
                var name = model.Bones[i].Name;

                menu.Items.Add(ListPanel.MenuItem(
                    new string('　', Depth(model, i)) + name,
                    () => Add(name),
                    !already.Contains(name)));
            }
        }

        private static int Depth(ModelData model, int bone)
        {
            var depth = 0;

            for (var parent = model.Bones[bone].Parent; parent >= 0; parent = model.Bones[parent].Parent)
            {
                if (++depth >= model.Bones.Length)
                    break;
            }

            return depth;
        }

        private void Add(string name)
        {
            if (target is not IBoneHost parameter)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);
            parameter.BoneFor(name);
            EndEdit?.Invoke(this, EventArgs.Empty);

            chosen = name;
        }

        private void RemoveSelected()
        {
            if (target is null || property is null || panel.List.SelectedItem is not ModelBonePose pose)
                return;

            var shown = panel.List.Items.Cast<ModelBonePose>().ToArray();
            var index = Array.IndexOf(shown, pose);

            chosen = shown.Length > 1 && index >= 0
                ? shown[index == shown.Length - 1 ? index - 1 : index + 1].Name
                : null;

            BeginEdit?.Invoke(this, EventArgs.Empty);
            property.SetValue(target, Poses.Remove(pose));
            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private void ResetSelected()
        {
            if (panel.List.SelectedItem is not ModelBonePose pose)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);
            Reset(pose);
            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private void ResetAll()
        {
            BeginEdit?.Invoke(this, EventArgs.Empty);

            foreach (var pose in Poses)
                Reset(pose);

            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private static void Reset(ModelBonePose pose)
        {
            foreach (var animation in new[] { pose.RotationX, pose.RotationY, pose.RotationZ })
            {
                foreach (var value in animation.Values)
                    value.Value = 0;
            }
        }

        private sealed class BoneLabel(Func<string, int> depth) : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
                => value is ModelBonePose pose
                    ? new string('　', depth(pose.Name)) + pose.Name
                    : string.Empty;

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
                => Binding.DoNothing;
        }

        private void OnBeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void OnEndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Clear()
        {
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
