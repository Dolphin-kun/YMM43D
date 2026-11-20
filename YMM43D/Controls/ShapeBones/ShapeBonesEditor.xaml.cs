using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ShapeBones
{
    public partial class ShapeBonesEditor : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ShapeBonesEditor()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        public void SetFocus() => BoneList.Focus();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ShapeBonesEditorViewModel before)
            {
                before.BeginEdit -= Slider_BeginEdit;
                before.EndEdit -= Slider_EndEdit;
            }

            if (e.NewValue is ShapeBonesEditorViewModel after)
            {
                after.BeginEdit += Slider_BeginEdit;
                after.EndEdit += Slider_EndEdit;
            }
        }

        private void Slider_BeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void Slider_EndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Text_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => BeginEdit?.Invoke(this, EventArgs.Empty);

        private void Text_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndEdit?.Invoke(this, EventArgs.Empty);

        private void BoneList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => ListWheel.PassOn(BoneList, e);
    }
}
