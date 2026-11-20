using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Input;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
using YMM43D.Project.Shape.Modifiers;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ModifierStack
{
    public sealed class ModifierStackEditorViewModel : ListEditorViewModel<ShapeModifier>
    {
        private const string AllCategory = "すべて";
        private const string Generate = "生成";
        private const string Deform = "変形";
        private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

        private static readonly IReadOnlyList<ModifierChoice> Choices =
        [
            new(ModifierKind.Mirror, "ミラー", Generate, ["左右対称", "反転", "mirror"]),
            new(ModifierKind.Subdivision, "細分化", Generate, ["サブディビジョンサーフェス", "なめらか", "subdivision", "subsurf"]),
            new(ModifierKind.Array, "配列", Generate, ["並べる", "繰り返し", "array"]),
            new(ModifierKind.Solidify, "厚み", Generate, ["ソリッド化", "厚みを付ける", "solidify"]),
            new(ModifierKind.Bevel, "ベベル", Generate, ["面取り", "角を削る", "bevel"]),
            new(ModifierKind.Boolean, "ブーリアン", Generate, ["削る", "合体", "重なり", "boolean"]),
            new(ModifierKind.Armature, "アーマチュア", Deform, ["骨", "ボーン", "armature", "bone"]),
        ];

        private ImmutableList<ShapeModifier>[] backups = [];
        private int pickAt = -1;
        private bool picking;

        public IReadOnlyList<string> Categories { get; } = [AllCategory, Generate, Deform];

        public string Category
        {
            get;
            set
            {
                if (Set(ref field, value))
                    Search = string.Empty;

                OnPropertyChanged(nameof(Filtered));
            }
        } = AllCategory;

        public string Search
        {
            get;
            set
            {
                if (Set(ref field, value ?? string.Empty))
                    OnPropertyChanged(nameof(Filtered));
            }
        } = string.Empty;

        public IEnumerable<ModifierChoice> Filtered => Choices.Where(Fits);

        public ModifierChoice? Picked
        {
            get;
            set
            {
                if (Set(ref field, value) && picking)
                    Preview(value);
            }
        }

        public IReadOnlyList<NamedChoice<BooleanOperation>> Operations { get; } =
        [
            new(BooleanOperation.Difference, "差分（削る）"),
            new(BooleanOperation.Union, "合体"),
            new(BooleanOperation.Intersect, "重なり"),
        ];

        public ICommand ApplyCommand { get; }

        public ModifierStackEditorViewModel(ItemProperty[] properties)
            : base(properties)
        {
            ApplyCommand = Command(() => HasSelection, Apply);
        }

        public void OpenPicker()
        {
            picking = false;
            Picked = null;
            pickAt = HasSelection ? SelectedIndex + 1 : Items.Count;
            backups = [.. Properties.Select(Read)];
            picking = true;
            BeginEditing();
        }

        public void ClosePicker()
        {
            if (!picking)
                return;

            picking = false;
            Picked = null;
            Search = string.Empty;
            EndEditing();
        }

        private bool Fits(ModifierChoice choice)
        {
            if (Search.Length == 0)
                return Category == AllCategory || choice.Category == Category;

            var compare = CultureInfo.CurrentCulture.CompareInfo;

            return new[] { choice.Name }.Concat(choice.Keywords).Any(word => compare.IndexOf(word, Search, Loose) >= 0);
        }

        private void Preview(ModifierChoice? choice)
        {
            for (var i = 0; i < Properties.Length; i++)
            {
                var before = backups[i];

                Properties[i].SetValue(choice is null ? before : before.Insert(Math.Min(pickAt, before.Count), Make(choice.Kind)));
            }

            Reload(choice is null ? pickAt - 1 : pickAt);
        }

        protected override ShapeModifier Create() => Make(ModifierKind.Mirror);

        private static ShapeModifier Make(ModifierKind kind) => kind switch
        {
            ModifierKind.Mirror => new MirrorModifier(),
            ModifierKind.Subdivision => new SubdivisionModifier(),
            ModifierKind.Array => new ArrayModifier(),
            ModifierKind.Solidify => new SolidifyModifier(),
            ModifierKind.Bevel => new BevelModifier(),
            ModifierKind.Armature => new ArmatureModifier(),
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
