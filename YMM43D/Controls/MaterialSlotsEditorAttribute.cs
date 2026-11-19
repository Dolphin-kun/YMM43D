using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class MaterialSlotsEditorAttribute : PropertyEditorAttribute2
    {
        public MaterialSlotsEditorAttribute()
        {
            PropertyEditorSize = PropertyEditorSize.FullWidth;
        }

        public override FrameworkElement Create() => new MaterialSlotsEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] properties)
        {
            if (control is MaterialSlotsEditor editor)
                editor.DataContext = new MaterialSlotsEditorViewModel(properties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not MaterialSlotsEditor editor)
                return;

            (editor.DataContext as IDisposable)?.Dispose();
            editor.DataContext = null;
        }
    }
}
