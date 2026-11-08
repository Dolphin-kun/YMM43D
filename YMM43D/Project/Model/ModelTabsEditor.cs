using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Resources.Icons;

namespace YMM43D.Project.Model
{
    public enum ModelTab
    {
        Materials,

        Bones,

        Faces,
    }

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class ModelTabsAttribute : PropertyEditorAttribute
    {
        public ModelTabsAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;

        public override FrameworkElement Create() => new ModelTabsEditor();

        public override void SetBindings(
            FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        {
            if (control is ModelTabsEditor editor)
                editor.Attach(propertyOwner as Model3DParameter);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is ModelTabsEditor editor)
                editor.Attach(null);
        }
    }

    internal sealed class ModelTabsEditor : ContentControl, IPropertyEditorControl
    {
        private static readonly (ModelTab Tab, object Icon, string Tip)[] Tabs =
        [
            (ModelTab.Materials, GeometryKeys.Palette, "材質"),
            (ModelTab.Bones, GeometryKeys.Bone, "ボーン"),
            (ModelTab.Faces, GeometryKeys.EmoticonOutline, "表情"),
        ];

        private readonly UniformGrid panel = new() { Rows = 1 };
        private readonly List<ToggleButton> buttons = [];

        private Model3DParameter? parameter;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ModelTabsEditor()
        {
            Content = panel;

            foreach (var (tab, icon, tip) in Tabs)
            {
                var button = new ToggleButton
                {
                    Content = ListPanel.Icon(icon, 16),
                    Padding = new Thickness(0, 4, 0, 4),
                    ToolTip = tip,
                    Tag = tab,
                };

                button.Click += (_, e) =>
                {
                    e.Handled = true;
                    Choose(tab);
                };

                buttons.Add(button);
                panel.Children.Add(button);
            }
        }

        public void SetFocus()
        {
        }

        public void Attach(Model3DParameter? owner)
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
            if (e.PropertyName is null
                or nameof(Model3DParameter.Tab)
                or nameof(Model3DParameter.HasBones)
                or nameof(Model3DParameter.HasFaces))
            {
                Refresh();
            }
        }

        private void Choose(ModelTab tab)
        {
            if (parameter is null)
            {
                Refresh();

                return;
            }

            BeginEdit?.Invoke(this, EventArgs.Empty);
            parameter.Tab = tab;
            EndEdit?.Invoke(this, EventArgs.Empty);

            Refresh();
        }

        private void Refresh()
        {
            foreach (var button in buttons)
            {
                var tab = (ModelTab)button.Tag;

                button.Visibility = Holds(tab) ? Visibility.Visible : Visibility.Collapsed;
                button.IsChecked = Chosen(tab);
            }
        }

        private bool Holds(ModelTab tab) => tab switch
        {
            ModelTab.Bones => parameter is { HasBones: true },
            ModelTab.Faces => parameter is { HasFaces: true },
            _ => parameter is not null,
        };

        private bool Chosen(ModelTab tab) => tab switch
        {
            ModelTab.Bones => parameter is { ShowsBones: true },
            ModelTab.Faces => parameter is { ShowsFaces: true },
            _ => parameter is { ShowsMaterials: true },
        };
    }
}
