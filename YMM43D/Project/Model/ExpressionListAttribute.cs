using System.Reflection;
using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class ExpressionListAttribute : PropertyEditorAttribute
    {
        public ExpressionListAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;

        public override FrameworkElement Create() => new ExpressionListEditor();

        public override void SetBindings(
            FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        {
            if (control is ExpressionListEditor editor)
                editor.Attach(item, propertyOwner, propertyInfo);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is ExpressionListEditor editor)
                editor.Detach();
        }
    }
}
