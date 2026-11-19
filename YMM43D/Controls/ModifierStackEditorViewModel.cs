using System.Windows.Input;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
using YMM43D.Project.Shape.Modifiers;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    public sealed class ModifierStackEditorViewModel : ListEditorViewModel<ShapeModifier>
    {
        public IReadOnlyList<NamedChoice<ModifierKind>> Kinds { get; } =
        [
            new(ModifierKind.Mirror, "ミラー"),
            new(ModifierKind.Subdivision, "細分化"),
            new(ModifierKind.Array, "配列"),
            new(ModifierKind.Solidify, "厚み"),
            new(ModifierKind.Bevel, "ベベル"),
            new(ModifierKind.Boolean, "ブーリアン"),
        ];

        public IReadOnlyList<NamedChoice<BooleanOperation>> Operations { get; } =
        [
            new(BooleanOperation.Difference, "差分（削る）"),
            new(BooleanOperation.Union, "合体"),
            new(BooleanOperation.Intersect, "重なり"),
        ];

        public ModifierKind NewKind { get; set => Set(ref field, value); }

        public ICommand ApplyCommand { get; }

        public ModifierStackEditorViewModel(ItemProperty[] properties)
            : base(properties)
        {
            ApplyCommand = Command(() => HasSelection, Apply);
        }

        protected override ShapeModifier Create() => NewKind switch
        {
            ModifierKind.Mirror => new MirrorModifier(),
            ModifierKind.Subdivision => new SubdivisionModifier(),
            ModifierKind.Array => new ArrayModifier(),
            ModifierKind.Solidify => new SolidifyModifier(),
            ModifierKind.Bevel => new BevelModifier(),
            _ => new BooleanModifier(),
        };

        private void Apply()
        {
            var at = SelectedIndex;

            Editing(() =>
            {
                foreach (var property in Properties)
                {
                    var list = Read(property);

                    if (at < list.Count && property.PropertyOwner is Shape3DParameter parameter)
                        parameter.ApplyModifier(list[at]);
                }
            });

            Reload(at);
        }
    }
}
