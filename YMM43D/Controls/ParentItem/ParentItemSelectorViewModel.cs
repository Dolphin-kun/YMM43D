using YMM43D.Player;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Controls.ParentItem
{
    public sealed class ParentItemSelectorViewModel : Bindable
    {
        private const string Missing = "見つからないアイテム";

        private readonly ItemProperty[] properties;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public IReadOnlyList<NamedChoice<string>> Choices { get; private set => Set(ref field, value); } = [];

        public string Selected
        {
            get => properties.Length > 0 ? properties[0].GetValue<string>() ?? string.Empty : string.Empty;
            set
            {
                if (value is null || value == Selected)
                    return;

                BeginEdit?.Invoke(this, EventArgs.Empty);

                foreach (var property in properties)
                    property.SetValue(value);

                EndEdit?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(Selected));
            }
        }

        public ParentItemSelectorViewModel(ItemProperty[] properties)
        {
            this.properties = properties;
            Refresh();
        }

        public void Refresh()
        {
            var selves = properties.Select(property => property.Item).OfType<IItem>().ToArray();
            var owners = properties.Select(property => property.PropertyOwner).OfType<ShapeParameter3DBase>().ToArray();
            List<NamedChoice<string>> made = [new(string.Empty, "なし")];

            if (selves.FirstOrDefault() is { } first && TimelineLookup.Holding(first) is { } timeline)
            {
                var shapes = timeline.Items.OfType<ShapeItem>().Where(item => item.ShapeParameter is ShapeParameter3DBase).ToArray();
                var byId = shapes.GroupBy(item => ((ShapeParameter3DBase)item.ShapeParameter).ItemId).ToDictionary(group => group.Key, group => group.First());

                foreach (var item in shapes.OrderBy(item => item.Layer).ThenBy(item => item.Frame))
                {
                    var shape = (ShapeParameter3DBase)item.ShapeParameter;

                    if (selves.Contains(item) || owners.Any(owner => Descends(shape, owner, byId)))
                        continue;

                    made.Add(new NamedChoice<string>(shape.ItemId, $"レイヤー {item.Layer}：{(item.Remark.Length > 0 ? item.Remark : item.Label)}"));
                }
            }

            var current = Selected;

            if (current.Length > 0 && made.All(choice => choice.Value != current))
                made.Add(new NamedChoice<string>(current, Missing));

            Choices = made;
            OnPropertyChanged(nameof(Selected));
        }

        private static bool Descends(ShapeParameter3DBase shape, ShapeParameter3DBase ancestor, Dictionary<string, ShapeItem> byId)
        {
            var at = shape;

            for (var depth = 0; depth <= byId.Count; depth++)
            {
                if (ReferenceEquals(at, ancestor) || at.ItemId == ancestor.ItemId)
                    return true;

                if (!at.HasParent || !byId.TryGetValue(at.ParentId, out var parent))
                    return false;

                at = (ShapeParameter3DBase)parent.ShapeParameter;
            }

            return false;
        }
    }
}
