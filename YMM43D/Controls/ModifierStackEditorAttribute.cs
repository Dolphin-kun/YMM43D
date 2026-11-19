using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ModifierStackEditorAttribute : PropertyEditorAttribute2
    {
        public ModifierStackEditorAttribute()
        {
            PropertyEditorSize = PropertyEditorSize.FullWidth;
        }

        public override FrameworkElement Create() => new ModifierStackEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] properties)
        {
            if (control is ModifierStackEditor editor)
                editor.DataContext = new ModifierStackEditorViewModel(properties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not ModifierStackEditor editor)
                return;

            (editor.DataContext as IDisposable)?.Dispose();
            editor.DataContext = null;
        }
    }
}
