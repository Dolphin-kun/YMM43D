using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows.Input;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    public sealed class ControlPointsEditorViewModel : Bindable, IDisposable
    {
        private const int LeastPoints = 2;

        private const double AppendStep = 100;

        private readonly ItemProperty[] properties;
        private readonly INotifyPropertyChanged[] owners;
        private readonly ActionCommand[] commands;

        private bool reloading;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ImmutableList<ControlPoint3D> Points
        {
            get;
            private set => Set(ref field, value);
        } = [];

        public int SelectedIndex
        {
            get;
            set
            {
                if (!Set(ref field, value))
                    return;

                OnSelectionChanged();

                if (!reloading && value >= 0)
                    ShareSelection(value);
            }
        } = -1;

        public bool HasSelection => SelectedIndex >= 0;

        public Animation[] SelectedX => Pick(point => point.X);

        public Animation[] SelectedY => Pick(point => point.Y);

        public Animation[] SelectedZ => Pick(point => point.Z);

        public ICommand AddCommand { get; }

        public ICommand RemoveCommand { get; }

        public ICommand MoveUpCommand { get; }

        public ICommand MoveDownCommand { get; }

        public ControlPointsEditorViewModel(ItemProperty[] properties)
        {
            this.properties = properties;

            var add = new ActionCommand(_ => properties.Length > 0, _ => Add());
            var remove = new ActionCommand(_ => HasSelection && Points.Count > LeastPoints, _ => Remove());
            var up = new ActionCommand(_ => SelectedIndex > 0, _ => Move(-1));
            var down = new ActionCommand(_ => HasSelection && SelectedIndex < Points.Count - 1, _ => Move(1));

            AddCommand = add;
            RemoveCommand = remove;
            MoveUpCommand = up;
            MoveDownCommand = down;
            commands = [add, remove, up, down];

            owners = [.. properties.Select(property => property.PropertyOwner).OfType<INotifyPropertyChanged>().Distinct()];

            foreach (var owner in owners)
                owner.PropertyChanged += OnOwnerChanged;

            Reload(Handles().Select(handles => handles.SelectedHandle).FirstOrDefault());
        }

        public void Dispose()
        {
            foreach (var owner in owners)
                owner.PropertyChanged -= OnOwnerChanged;
        }

        private ImmutableList<ControlPoint3D> Read(ItemProperty property)
            => property.GetValue<ImmutableList<ControlPoint3D>>() ?? [];

        private IEnumerable<IScenePointHandles> Handles()
            => properties.Select(property => property.PropertyOwner).OfType<IScenePointHandles>().Distinct();

        private Animation[] Pick(Func<ControlPoint3D, Animation> axis)
        {
            if (!HasSelection)
                return [];

            return [.. properties
                .Select(Read)
                .Where(list => SelectedIndex < list.Count)
                .Select(list => axis(list[SelectedIndex]))];
        }

        private void Reload(int select)
        {
            reloading = true;

            try
            {
                Points = properties.Length > 0 ? Read(properties[0]) : [];
                SelectedIndex = Points.Count == 0 ? -1 : Math.Clamp(select, 0, Points.Count - 1);
            }
            finally
            {
                reloading = false;
            }

            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedX));
            OnPropertyChanged(nameof(SelectedY));
            OnPropertyChanged(nameof(SelectedZ));

            foreach (var command in commands)
                command.RaiseCanExecuteChanged();
        }

        private void ShareSelection(int index)
        {
            foreach (var handles in Handles())
            {
                if (handles.SelectedHandle != index)
                    handles.SelectedHandle = index;
            }
        }

        private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (properties.Length == 0)
                return;

            if (!ReferenceEquals(Read(properties[0]), Points))
            {
                Reload(SelectedIndex);
                return;
            }

            if (e.PropertyName == nameof(IScenePointHandles.SelectedHandle)
                && sender is IScenePointHandles handles
                && handles.SelectedHandle >= 0
                && handles.SelectedHandle < Points.Count)
            {
                SelectedIndex = handles.SelectedHandle;
            }
        }

        private void Add()
        {
            var at = HasSelection ? SelectedIndex : Points.Count - 1;

            Edit(list =>
            {
                if (list.Count == 0)
                    return [new ControlPoint3D()];

                var from = Math.Min(at, list.Count - 1);
                var made = from + 1 < list.Count
                    ? ControlPoint3D.Between(list[from], list[from + 1])
                    : ControlPoint3D.After(list[from], AppendStep);

                return list.Insert(from + 1, made);
            }, at + 1);
        }

        private void Remove()
        {
            var at = SelectedIndex;

            Edit(list => list.Count > LeastPoints && at < list.Count ? list.RemoveAt(at) : list, at);
        }

        private void Move(int step)
        {
            var at = SelectedIndex;
            var to = at + step;

            Edit(list =>
            {
                if (at < 0 || to < 0 || at >= list.Count || to >= list.Count)
                    return list;

                return list.SetItem(at, list[to]).SetItem(to, list[at]);
            }, to);
        }

        private void Edit(Func<ImmutableList<ControlPoint3D>, ImmutableList<ControlPoint3D>> change, int select)
        {
            BeginEdit?.Invoke(this, EventArgs.Empty);

            try
            {
                foreach (var property in properties)
                {
                    var before = Read(property);
                    var after = change(before);

                    if (!ReferenceEquals(before, after))
                        property.SetValue(after);
                }
            }
            finally
            {
                EndEdit?.Invoke(this, EventArgs.Empty);
            }

            Reload(select);

            if (HasSelection)
                ShareSelection(SelectedIndex);
        }
    }
}
