using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ShapeKeysEditorAttribute : PropertyEditorAttribute2
    {
        public ShapeKeysEditorAttribute()
        {
            PropertyEditorSize = PropertyEditorSize.FullWidth;
        }

        public override FrameworkElement Create() => new ShapeKeysEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] properties)
        {
            if (control is ShapeKeysEditor editor)
                editor.DataContext = new ShapeKeysEditorViewModel(properties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not ShapeKeysEditor editor)
                return;

            (editor.DataContext as IDisposable)?.Dispose();
            editor.DataContext = null;
        }
    }
}
