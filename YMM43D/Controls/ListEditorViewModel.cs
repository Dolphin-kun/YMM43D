using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls
{
    public abstract class ListEditorViewModel<T> : Bindable, IDisposable where T : class
    {
        private readonly INotifyPropertyChanged[] owners;
        private readonly List<ActionCommand> commands = [];

        private bool reloading;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ImmutableList<T> Items
        {
            get;
            private set => Set(ref field, value);
        } = [];

        public int SelectedIndex
        {
            get;
            set
            {
                if (Set(ref field, value) && !reloading)
                    OnSelectionChanged();
            }
        } = -1;

        public T? Selected => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

        public bool HasSelection => Selected is not null;

        public ICommand AddCommand { get; }

        public ICommand RemoveCommand { get; }

        public ICommand MoveUpCommand { get; }

        public ICommand MoveDownCommand { get; }

        protected ItemProperty[] Properties { get; }

        protected ListEditorViewModel(ItemProperty[] properties)
        {
            Properties = properties;

            AddCommand = Command(() => properties.Length > 0, Add);
            RemoveCommand = Command(() => HasSelection, Remove);
            MoveUpCommand = Command(() => SelectedIndex > 0, () => Move(-1));
            MoveDownCommand = Command(() => HasSelection && SelectedIndex < Items.Count - 1, () => Move(1));

            owners = [.. properties.Select(property => property.PropertyOwner).OfType<INotifyPropertyChanged>().Distinct()];

            foreach (var owner in owners)
                owner.PropertyChanged += OnOwnerChanged;

            Reload(0);
        }

        public void Dispose()
        {
            foreach (var owner in owners)
                owner.PropertyChanged -= OnOwnerChanged;

            GC.SuppressFinalize(this);
        }

        protected abstract T Create();

        protected static ImmutableList<T> Read(ItemProperty property) => property.GetValue<ImmutableList<T>>() ?? [];

        protected ActionCommand Command(Func<bool> can, Action run)
        {
            var command = new ActionCommand(_ => can(), _ => run());

            commands.Add(command);

            return command;
        }

        protected void Reload(int select)
        {
            reloading = true;

            try
            {
                Items = Properties.Length > 0 ? Read(Properties[0]) : [];
                SelectedIndex = Items.Count == 0 ? -1 : Math.Clamp(select, 0, Items.Count - 1);
            }
            finally
            {
                reloading = false;
            }

            OnSelectionChanged();
        }

        protected void Editing(Action work)
        {
            BeginEdit?.Invoke(this, EventArgs.Empty);

            try
            {
                work();
            }
            finally
            {
                EndEdit?.Invoke(this, EventArgs.Empty);
            }
        }

        protected void Edit(Func<ImmutableList<T>, ImmutableList<T>> change, int select)
        {
            Editing(() =>
            {
                foreach (var property in Properties)
                {
                    var before = Read(property);
                    var after = change(before);

                    if (!ReferenceEquals(before, after))
                        property.SetValue(after);
                }
            });

            Reload(select);
        }

        private void OnSelectionChanged()
        {
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasSelection));

            foreach (var command in commands)
                command.RaiseCanExecuteChanged();
        }

        private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Properties.Length > 0 && !ReferenceEquals(Read(Properties[0]), Items))
                Reload(SelectedIndex);
        }

        protected virtual void Add() => Edit(list => list.Add(Create()), Items.Count);

        protected virtual void Remove()
        {
            var at = SelectedIndex;

            Edit(list => at < list.Count ? list.RemoveAt(at) : list, at);
        }

        protected virtual void Move(int step)
        {
            var at = SelectedIndex;
            var to = at + step;

            Edit(list => at < 0 || to < 0 || at >= list.Count || to >= list.Count ? list : list.SetItem(at, list[to]).SetItem(to, list[at]), to);
        }
    }
}
