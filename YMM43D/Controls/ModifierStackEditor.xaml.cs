using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    public partial class ModifierStackEditor : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ModifierStackEditor()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        public void SetFocus() => ModifierList.Focus();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ModifierStackEditorViewModel before)
            {
                before.BeginEdit -= Slider_BeginEdit;
                before.EndEdit -= Slider_EndEdit;
            }

            if (e.NewValue is ModifierStackEditorViewModel after)
            {
                after.BeginEdit += Slider_BeginEdit;
                after.EndEdit += Slider_EndEdit;
            }
        }

        private void Slider_BeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void Slider_EndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void ModifierList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => ListWheel.PassOn(ModifierList, e);
    }
}
