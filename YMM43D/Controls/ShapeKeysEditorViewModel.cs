using YMM43D.Project.Shape;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    public sealed class ShapeKeysEditorViewModel(ItemProperty[] properties) : ListEditorViewModel<ShapeKey>(properties)
    {
        protected override ShapeKey Create() => new();

        protected override void Add() => Change(parameter => parameter.AddShapeKey(), Items.Count);

        protected override void Remove()
        {
            var at = SelectedIndex;

            Change(parameter => parameter.RemoveShapeKey(at), at);
        }

        protected override void Move(int step)
        {
            var at = SelectedIndex;

            Change(parameter => parameter.MoveShapeKey(at, at + step), at + step);
        }

        private void Change(Action<Shape3DParameter> work, int select)
        {
            Editing(() =>
            {
                foreach (var property in Properties)
                {
                    if (property.PropertyOwner is Shape3DParameter parameter)
                        work(parameter);
                }
            });

            Reload(select);
        }
    }
}
