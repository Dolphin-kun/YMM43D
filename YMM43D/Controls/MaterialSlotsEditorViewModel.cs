using YMM43D.Project.Shape;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    public sealed class MaterialSlotsEditorViewModel(ItemProperty[] properties) : ListEditorViewModel<ShapeMaterial>(properties)
    {
        protected override ShapeMaterial Create() => new() { Name = $"材質 {Items.Count + 1}" };
    }
}
