using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool
{
    internal sealed class AmountRedo(
        Shape3DParameter parameter,
        MeshTopology original,
        ChosenParts parts,
        AmountSpec spec,
        float scale,
        Func<MeshTopology, ChosenParts, AmountValue, (MeshTopology Mesh, int[] Added)?> work,
        MeshSelection selection,
        AmountValue value,
        MeshTopology current) : Bindable
    {
        private MeshTopology current = current;
        private AmountValue value = value;

        public event Action? Applied;

        public bool IsCurrent => ReferenceEquals(parameter.ShownMesh(), current);

        public string Title => spec.Label;

        public bool HasAmount => spec.Unit is not AmountUnit.Count;

        public bool IsAngle => spec.Unit is AmountUnit.Angle;

        public bool IsFactor => spec.Unit is AmountUnit.Factor;

        public string AmountLabel => IsAngle ? "角度" : "量";

        public string AmountUnitText => IsAngle ? "°" : IsFactor ? string.Empty : "px";

        public string AmountFormat => IsFactor ? "F2" : "F1";

        public double AmountDelta => IsFactor ? 0.01 : 0.1;

        public double AmountMin => IsFactor ? 0 : spec.Signed || IsAngle ? -100000 : 0;

        public double AmountMax => IsFactor ? 1 : 100000;

        public double AmountDefaultMin => IsAngle ? -360 : spec.Signed ? -200 : 0;

        public double AmountDefaultMax => IsAngle ? 360 : IsFactor ? 1 : 200;

        public double Amount
        {
            get => Math.Round(IsAngle ? float.RadiansToDegrees(value.Amount) : IsFactor ? value.Amount : WorldScale.ToPixels(value.Amount * scale), 2);
            set
            {
                var wanted = IsAngle ? float.DegreesToRadians((float)value) : IsFactor ? Math.Clamp((float)value, 0f, 1f) : WorldScale.ToWorld((float)value) / scale;

                Apply(this.value with { Amount = spec.Signed || IsAngle || IsFactor ? wanted : MathF.Max(wanted, 0f) });
            }
        }

        public bool HasCount => spec.CountLabel.Length > 0;

        public string CountLabel => spec.CountLabel;

        public double CountMin => spec.LeastCount;

        public double Count
        {
            get => value.Count;
            set => Apply(this.value with { Count = Math.Max(spec.LeastCount, (int)Math.Round(value)) });
        }

        public bool HasShift => spec.ShiftLabel.Length > 0;

        public string ShiftLabel => spec.ShiftLabel;

        public double Shift
        {
            get => value.Shift;
            set => Apply(this.value with { Shift = (int)Math.Round(value) });
        }

        private void Apply(AmountValue wanted)
        {
            if (wanted == value || !IsCurrent || work(original, parts, wanted) is not { } made)
            {
                OnPropertyChanged(string.Empty);
                return;
            }

            value = wanted;
            current = parameter.StoreMesh(made.Mesh.Carry(original));
            selection.Replace(current, made.Added);

            OnPropertyChanged(string.Empty);
            Applied?.Invoke();
        }
    }
}
