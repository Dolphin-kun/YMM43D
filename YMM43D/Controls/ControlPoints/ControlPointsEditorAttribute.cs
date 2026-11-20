using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ControlPoints
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ControlPointsEditorAttribute : PropertyEditorAttribute2
    {
        public ControlPointsEditorAttribute()
        {
            PropertyEditorSize = PropertyEditorSize.FullWidth;
        }

        public override FrameworkElement Create() => new ControlPointsEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] properties)
        {
            if (control is ControlPointsEditor editor)
                editor.DataContext = new ControlPointsEditorViewModel(properties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not ControlPointsEditor editor)
                return;

            (editor.DataContext as IDisposable)?.Dispose();
            editor.DataContext = null;
        }
    }
}
