using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ParentItem
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ParentItemSelectorAttribute : PropertyEditorAttribute2
    {
        public override FrameworkElement Create() => new ParentItemSelector();

        public override void SetBindings(FrameworkElement control, ItemProperty[] properties)
        {
            if (control is ParentItemSelector selector)
                selector.DataContext = new ParentItemSelectorViewModel(properties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is ParentItemSelector selector)
                selector.DataContext = null;
        }
    }
}
