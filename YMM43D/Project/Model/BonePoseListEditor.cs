using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Resources.Icons;

namespace YMM43D.Project.Model
{
    // 「骨」の一覧を、骨ごとに折りたためる形で出す。
    // 中身の編集欄は YMM4 のいつものものをそのまま使い、名前は折りたたみの見出しにする。
    internal sealed class BonePoseListEditor : ContentControl, IPropertyEditorControl, IPropertyEditorControl2
    {
        // 中身は「回転X/Y/Z」だけ。並び順はパラメータに書いてある順。
        private static readonly string[] Shown =
            [nameof(ModelBonePose.RotationX), nameof(ModelBonePose.RotationY), nameof(ModelBonePose.RotationZ)];

        private static readonly PropertiesCache cache = new();

        private readonly StackPanel panel = new();
        private readonly List<(PropertyEditorAttribute2 Attribute, FrameworkElement Control)> editors = [];

        // 骨は開いた状態で出す。閉じた骨だけ覚えておいて、並べ直しても閉じたままにする。
        private readonly HashSet<string> closed = [];

        private object? item;
        private object? target;
        private PropertyInfo? property;
        private IEditorInfo? editorInfo;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public BonePoseListEditor() => Content = panel;

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

        private ImmutableList<ModelBonePose> Poses
            => target is not null && property?.GetValue(target) is ImmutableList<ModelBonePose> list
                ? list
                : [];

        private void Rebuild()
        {
            Clear();

            var poses = Poses;

            if (poses.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "3Dプレビューで骨をクリックすると、ここに並びます。",
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2),
                });

                return;
            }

            foreach (var pose in poses)
                panel.Children.Add(Row(pose));
        }

        private UIElement Row(ModelBonePose pose)
        {
            var remove = new Button
            {
                Content = DeleteMark(),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "この骨の回転をやめる",
            };

            remove.Click += (_, _) => Remove(pose);

            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(remove, Dock.Right);
            header.Children.Add(remove);

            header.Children.Add(new TextBlock
            {
                Text = pose.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = pose.Name,
            });

            var expander = new Expander
            {
                Header = header,
                Content = Values(pose),
                IsExpanded = !closed.Contains(pose.Name),
            };

            expander.Expanded += (_, _) => closed.Remove(pose.Name);
            expander.Collapsed += (_, _) => closed.Add(pose.Name);

            return expander;
        }

        // ゴミ箱の絵は YMM4 本体が持っているものを使う。見つからないときは文字で出す。
        private FrameworkElement DeleteMark()
        {
            if ((TryFindResource(GeometryKeys.TrashCanOutline)
                ?? Application.Current?.TryFindResource(GeometryKeys.TrashCanOutline)) is not Geometry found)
            {
                return new TextBlock { Text = "✕" };
            }

            var mark = new Path
            {
                Data = found,
                Stretch = Stretch.Uniform,
                Width = 13,
                Height = 13,
            };

            mark.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) },
            });

            return mark;
        }

        // 骨 1 本ぶんの編集欄。いつもの編集欄をそのまま作って並べる。
        private UIElement Values(ModelBonePose pose)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 6) };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            foreach (var name in Shown)
            {
                if (typeof(ModelBonePose).GetProperty(name) is not { } info
                    || cache.GetCustomAttribute<PropertyEditorAttribute2>(info) is not { } attribute)
                {
                    continue;
                }

                var control = attribute.Create();

                attribute.SetBindings(control, [new ItemProperty(item ?? pose, pose, info, cache)]);

                if (control is IPropertyEditorControl2 told && editorInfo is { } known)
                    told.SetEditorInfo(known);

                if (control is IPropertyEditorControl edited)
                {
                    edited.BeginEdit += OnBeginEdit;
                    edited.EndEdit += OnEndEdit;
                }

                editors.Add((attribute, control));

                var label = new TextBlock
                {
                    Text = cache.GetCustomAttribute<DisplayAttribute>(info)?.Name ?? name,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 8, 0),
                };

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var row = grid.RowDefinitions.Count - 1;

                Grid.SetRow(label, row);
                Grid.SetRow(control, row);
                Grid.SetColumn(control, 1);

                grid.Children.Add(label);
                grid.Children.Add(control);
            }

            return grid;
        }

        private void Remove(ModelBonePose pose)
        {
            if (target is null || property is null)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);
            property.SetValue(target, Poses.Remove(pose));
            EndEdit?.Invoke(this, EventArgs.Empty);
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
