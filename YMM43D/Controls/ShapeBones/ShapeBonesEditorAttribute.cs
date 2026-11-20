using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ShapeBones
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ShapeBonesEditorAttribute : PropertyEditorAttribute2
    {
        public ShapeBonesEditorAttribute()
        {
            PropertyEditorSize = PropertyEditorSize.FullWidth;
        }

        public override FrameworkElement Create() => new ShapeBonesEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] properties)
        {
            if (control is ShapeBonesEditor editor)
                editor.DataContext = new ShapeBonesEditorViewModel(properties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not ShapeBonesEditor editor)
                return;

            (editor.DataContext as IDisposable)?.Dispose();
            editor.DataContext = null;
        }
    }
}
