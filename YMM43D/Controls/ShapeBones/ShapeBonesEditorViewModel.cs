using System.Numerics;
using System.Windows.Input;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ShapeBones
{
    public sealed class ShapeBonesEditorViewModel : ListEditorViewModel<ShapeBone>
    {
        private const double FirstLength = 100;

        public ICommand AutoWeightsCommand { get; }

        public IReadOnlyList<NamedChoice<string>> ParentChoices => [new(string.Empty, "なし"), .. Items.Select(bone => new NamedChoice<string>(bone.Name, bone.Name))];

        public ShapeBonesEditorViewModel(ItemProperty[] properties)
            : base(properties)
        {
            AutoWeightsCommand = Command(() => Items.Count > 0, () => Change(parameter => parameter.AutoWeights(), SelectedIndex));
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Items))
                    OnPropertyChanged(nameof(ParentChoices));
            };
        }

        protected override ShapeBone Create() => new();

        protected override void Add()
        {
            var bone = Grown();

            Change(parameter => parameter.AddBone(bone), Items.Count);
        }

        protected override void Remove()
        {
            var at = SelectedIndex;

            Change(parameter => parameter.RemoveBone(at), at);
        }

        protected override void Move(int step)
        {
            var at = SelectedIndex;

            Change(parameter => parameter.MoveBone(at, at + step), at + step);
        }

        private ShapeBone Grown()
        {
            var name = Enumerable.Range(Items.Count + 1, Items.Count + 2).Select(number => $"骨 {number}").First(candidate => Items.All(bone => bone.Name != candidate));

            if (Selected is not { } from)
                return new ShapeBone { Name = name, TailY = -FirstLength };

            var head = new Vector3((float)from.TailX, (float)from.TailY, (float)from.TailZ);
            var span = head - new Vector3((float)from.HeadX, (float)from.HeadY, (float)from.HeadZ);
            var tail = head + (span.LengthSquared() > 1e-6f ? span : new Vector3(0f, -(float)FirstLength, 0f));

            return new ShapeBone
            {
                Name = name,
                Parent = from.Name,
                HeadX = head.X,
                HeadY = head.Y,
                HeadZ = head.Z,
                TailX = tail.X,
                TailY = tail.Y,
                TailZ = tail.Z,
            };
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
