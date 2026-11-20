using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.MaterialSlots
{
    public partial class MaterialSlotsEditor : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public MaterialSlotsEditor()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        public void SetFocus() => MaterialList.Focus();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MaterialSlotsEditorViewModel before)
            {
                before.BeginEdit -= Slider_BeginEdit;
                before.EndEdit -= Slider_EndEdit;
            }

            if (e.NewValue is MaterialSlotsEditorViewModel after)
            {
                after.BeginEdit += Slider_BeginEdit;
                after.EndEdit += Slider_EndEdit;
            }
        }

        private void Slider_BeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void Slider_EndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Text_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => BeginEdit?.Invoke(this, EventArgs.Empty);

        private void Text_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndEdit?.Invoke(this, EventArgs.Empty);

        private void MaterialList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => ListWheel.PassOn(MaterialList, e);
    }
}
