using System.Numerics;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool
{
    internal sealed class EditSidebar : Bindable
    {
        private Vector3 center;
        private Vector3 cursor;
        private double radius;

        public event Action<Vector3>? CenterMoved;

        public event Action<Vector3>? CursorMoved;

        public event Action<float>? RadiusChanged;

        public event Action<int>? MaterialAssigned;

        public event Action<int, bool>? MaterialChosen;

        public event Action<int>? KeyChosen;

        public EditSidebar()
        {
            AssignMaterialCommand = new ActionCommand(_ => HasSelection && MaterialIndex >= 0, _ => MaterialAssigned?.Invoke(MaterialIndex));
            SelectMaterialCommand = new ActionCommand(_ => MaterialIndex >= 0, _ => MaterialChosen?.Invoke(MaterialIndex, true));
            DeselectMaterialCommand = new ActionCommand(_ => MaterialIndex >= 0, _ => MaterialChosen?.Invoke(MaterialIndex, false));
        }

        public IReadOnlyList<string> Materials { get; private set => Set(ref field, value, nameof(Materials), nameof(HasMaterials)); } = [];

        public bool HasMaterials => Materials.Count > 0;

        public int MaterialIndex
        {
            get;
            set
            {
                if (Set(ref field, value))
                    RefreshCommands();
            }
        } = -1;

        public IReadOnlyList<string> ShapeKeys { get; private set => Set(ref field, value, nameof(ShapeKeys), nameof(HasShapeKeys)); } = [];

        public bool HasShapeKeys => ShapeKeys.Count > 1;

        public int ShapeKeyIndex
        {
            get;
            set
            {
                if (Set(ref field, value) && value >= 0 && !showing)
                    KeyChosen?.Invoke(value - 1);
            }
        }

        private bool showing;

        public ActionCommand AssignMaterialCommand { get; }

        public ActionCommand SelectMaterialCommand { get; }

        public ActionCommand DeselectMaterialCommand { get; }

        public bool IsEditing { get; private set => Set(ref field, value, nameof(IsEditing), nameof(IsIdle)); }

        public bool IsIdle => !IsEditing;

        public AmountRedo? Redo { get; set => Set(ref field, value, nameof(Redo), nameof(HasRedo)); }

        public bool HasRedo => Redo is not null;

        public bool HasSelection { get; private set => Set(ref field, value); }

        public event Action<Vector2>? UvMoved;

        public double CenterX
        {
            get => Math.Round(center.X, 2);
            set => MoveCenter(center with { X = (float)value });
        }

        public double CenterY
        {
            get => Math.Round(center.Y, 2);
            set => MoveCenter(center with { Y = (float)value });
        }

        public double CenterZ
        {
            get => Math.Round(center.Z, 2);
            set => MoveCenter(center with { Z = (float)value });
        }

        public double CursorX
        {
            get => Math.Round(cursor.X, 2);
            set => MoveCursor(cursor with { X = (float)value });
        }

        public double CursorY
        {
            get => Math.Round(cursor.Y, 2);
            set => MoveCursor(cursor with { Y = (float)value });
        }

        public double CursorZ
        {
            get => Math.Round(cursor.Z, 2);
            set => MoveCursor(cursor with { Z = (float)value });
        }

        public string HiddenText { get; private set => Set(ref field, value, nameof(HiddenText), nameof(HasHidden)); } = string.Empty;

        public bool HasHidden => HiddenText.Length > 0;

        public void ShowHidden(string text)
        {
            if (text != HiddenText)
                HiddenText = text;
        }

        public bool IsUv { get; private set => Set(ref field, value); }

        public bool HasUvSelection { get; private set => Set(ref field, value); }

        public double UvU
        {
            get => Math.Round(uvCenter.X, 4);
            set => MoveUv(uvCenter with { X = (float)value });
        }

        public double UvV
        {
            get => Math.Round(uvCenter.Y, 4);
            set => MoveUv(uvCenter with { Y = (float)value });
        }

        private Vector2 uvCenter;

        private void MoveCenter(Vector3 to)
        {
            center = to;
            Announce(nameof(CenterX), nameof(CenterY), nameof(CenterZ));
            CenterMoved?.Invoke(to);
        }

        private void MoveCursor(Vector3 to)
        {
            cursor = to;
            Announce(nameof(CursorX), nameof(CursorY), nameof(CursorZ));
            CursorMoved?.Invoke(to);
        }

        private void MoveUv(Vector2 to)
        {
            uvCenter = to;
            Announce(nameof(UvU), nameof(UvV));
            UvMoved?.Invoke(to);
        }

        private void Announce(params string[] names)
        {
            foreach (var name in names)
                OnPropertyChanged(name);
        }

        public bool IsProportional { get; private set => Set(ref field, value); }

        public double Radius
        {
            get => radius;
            set
            {
                if (value <= 0)
                    return;

                radius = value;
                OnPropertyChanged(nameof(Radius));
                RadiusChanged?.Invoke((float)value);
            }
        }

        public void Show(bool editing, Vector3? chosenCenter, Vector3 cursorAt, float? reach, IReadOnlyList<string> materials, IReadOnlyList<string> keys, int activeKey, bool uv, Vector2? uvAt)
        {
            IsEditing = editing;
            IsUv = uv;
            HasUvSelection = uvAt is not null;

            if (uvAt is { } placedUv && placedUv != uvCenter)
            {
                uvCenter = placedUv;
                Announce(nameof(UvU), nameof(UvV));
            }

            showing = true;

            if (!keys.SequenceEqual(ShapeKeys))
                ShapeKeys = keys;

            ShapeKeyIndex = keys.Count == 0 ? -1 : Math.Clamp(activeKey + 1, 0, keys.Count - 1);
            showing = false;

            if (HasSelection != chosenCenter is not null)
            {
                HasSelection = chosenCenter is not null;
                RefreshCommands();
            }

            if (!materials.SequenceEqual(Materials))
            {
                var keep = MaterialIndex;

                Materials = materials;
                MaterialIndex = materials.Count == 0 ? -1 : Math.Clamp(keep, 0, materials.Count - 1);
            }

            if (chosenCenter is { } placed && placed != center)
            {
                center = placed;
                OnPropertyChanged(nameof(CenterX));
                OnPropertyChanged(nameof(CenterY));
                OnPropertyChanged(nameof(CenterZ));
            }

            if (cursorAt != cursor)
            {
                cursor = cursorAt;
                OnPropertyChanged(nameof(CursorX));
                OnPropertyChanged(nameof(CursorY));
                OnPropertyChanged(nameof(CursorZ));
            }

            IsProportional = reach is not null;

            if (reach is { } shown && Math.Abs(Math.Round(shown, 1) - radius) > 1e-6)
            {
                radius = Math.Round(shown, 1);
                OnPropertyChanged(nameof(Radius));
            }
        }

        private void RefreshCommands()
        {
            AssignMaterialCommand.RaiseCanExecuteChanged();
            SelectMaterialCommand.RaiseCanExecuteChanged();
            DeselectMaterialCommand.RaiseCanExecuteChanged();
        }
    }
}
