using System.Reflection;
using System.Windows;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    // 「骨」の一覧に付ける編集欄。骨ごとに折りたためる形で出す。
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class BonePoseListAttribute : PropertyEditorAttribute
    {
        public BonePoseListAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;

        public override FrameworkElement Create() => new BonePoseListEditor();

        public override void SetBindings(
            FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        {
            if (control is BonePoseListEditor editor)
                editor.Attach(item, propertyOwner, propertyInfo);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is BonePoseListEditor editor)
                editor.Detach();
        }
    }
}
