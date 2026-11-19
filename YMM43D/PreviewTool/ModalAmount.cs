using System.Numerics;
using System.Windows.Input;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;

namespace YMM43D.PreviewTool
{
    internal sealed class ModalAmount
    {
        private readonly TypedNumber typed = new();

        private Func<MeshTopology, ChosenParts, AmountValue, (MeshTopology Mesh, int[] Added)?>? work;
        private ChosenParts parts = new([], [], []);
        private Shape3DParameter? parameter;
        private MeshTopology? original;
        private MeshSelection? selection;
        private (MeshTopology Mesh, int[] Added)? made;
        private AmountSpec spec;
        private float scale = 1f;
        private AmountValue last;
        private AmountRedo? finished;
        private float lastAngle;
        private float turned;
        private int count;
        private int shift;
        private Vector2 startMouse;

        public bool IsActive => parameter is not null;

        public Vector2 PivotScreen { get; private set; }

        public Vector2 Mouse { get; private set; }

        public bool ShowsGuide => IsActive && spec.Unit is not AmountUnit.Count;

        public string Status { get; private set; } = string.Empty;

        public bool Begin(
            in AmountSpec wanted,
            Func<MeshTopology, ChosenParts, AmountValue, (MeshTopology Mesh, int[] Added)?> operation,
            in MeshEditTarget edit,
            MeshSelection chosen,
            in Vector2 at,
            in ViewSnapshot view)
        {
            if (edit.Item.IsLocked || (wanted.Unit is AmountUnit.Length && wanted.Start <= 0f))
                return false;

            var mesh = edit.Parameter.EnsureMesh();

            chosen.Bind(mesh);

            var frozen = new ChosenParts([.. chosen.Vertices], [.. chosen.Edges], [.. chosen.Faces]);

            if (operation(mesh, frozen, new AmountValue(wanted.Start, wanted.Count, 0)) is null)
                return false;

            work = operation;
            parts = frozen;
            parameter = edit.Parameter;
            original = mesh;
            selection = chosen;
            spec = wanted;
            count = wanted.Count;
            shift = 0;
            turned = 0f;
            scale = MathF.Max((Length(edit.ToWorld, Vector3.UnitX) + Length(edit.ToWorld, Vector3.UnitY) + Length(edit.ToWorld, Vector3.UnitZ)) / 3f, 1e-6f);
            startMouse = Mouse = at;
            PivotScreen = (wanted.Around is { } around ? view.ToScreen(around) : view.ToScreen(MeshPivot.Median(mesh, edit.ToWorld, chosen.Vertices))) ?? at;
            lastAngle = AngleOf(at);
            typed.Clear();
            made = null;

            return true;
        }

        public void Update(in Vector2 at, in SnapSettings snap)
        {
            if (work is null || original is null || parameter is null)
                return;

            Mouse = at;

            var amount = spec.Unit switch
            {
                AmountUnit.Angle => Angle(at, snap),
                AmountUnit.Count => spec.Start,
                AmountUnit.Factor => Factor(at),
                _ => Distance(at, snap),
            };

            Status = $"{spec.Label}{AmountText(amount)}{CountText()}{TypedText()}";

            last = new AmountValue(amount, count, shift);

            if ((spec.Unit is AmountUnit.Length && MathF.Abs(amount) <= 1e-6f) || work(original, parts, last) is not { } result)
            {
                made = null;
                parameter.StoreMesh(original);
                return;
            }

            made = (result.Mesh.Carry(original), result.Added);
            parameter.StoreMesh(made.Value.Mesh);
        }

        public void Step(int delta, bool shifts)
        {
            if (!IsActive)
                return;

            if (shifts && spec.ShiftLabel.Length > 0)
                shift += delta;
            else if (spec.CountLabel.Length > 0)
                count = Math.Max(spec.LeastCount, count + delta);
        }

        public ModalResult Press(Key key)
        {
            if (!IsActive)
                return ModalResult.Ignored;

            switch (key)
            {
                case Key.Escape:
                    Cancel();
                    return ModalResult.Cancelled;

                case Key.Enter:
                    Confirm();
                    return ModalResult.Confirmed;

                case Key.PageUp:
                    Step(1, false);
                    return ModalResult.Handled;

                case Key.PageDown:
                    Step(-1, false);
                    return ModalResult.Handled;

                default:
                    typed.Take(key);

                    if (spec.Unit is AmountUnit.Count && typed.HasValue)
                        count = Math.Max(spec.LeastCount, (int)MathF.Round(typed.Value));

                    return ModalResult.Handled;
            }
        }

        public void Confirm()
        {
            if (made is { } result && selection is not null && parameter is not null && original is not null && work is not null)
            {
                selection.Replace(result.Mesh, result.Added);
                finished = new AmountRedo(parameter, original, parts, spec, scale, work, selection, last, result.Mesh);
            }

            End();
        }

        public AmountRedo? TakeFinished()
        {
            var taken = finished;

            finished = null;

            return taken;
        }

        public void Cancel()
        {
            if (parameter is not null && original is not null)
                parameter.StoreMesh(original);

            End();
        }

        private float Distance(in Vector2 at, in SnapSettings snap)
        {
            float amount;

            if (typed.HasValue)
            {
                amount = WorldScale.ToWorld(typed.Value) / scale;
            }
            else
            {
                var start = MathF.Max(Vector2.Distance(startMouse, PivotScreen), 1f);
                var ratio = Vector2.Distance(at, PivotScreen) / start;

                amount = spec.Start * ratio;

                if (snap.SnapsToIncrement)
                    amount = snap.Grid.SnapLength(amount * scale) / scale;
            }

            return spec.Signed ? amount : MathF.Max(amount, 0f);
        }

        private float Factor(in Vector2 at)
        {
            if (typed.HasValue)
                return Math.Clamp(typed.Value, 0f, 1f);

            var start = MathF.Max(Vector2.Distance(startMouse, PivotScreen), 1f);

            return Math.Clamp(spec.Start + Vector2.Distance(at, PivotScreen) / start - 1f, 0f, 1f);
        }

        private float Angle(in Vector2 at, in SnapSettings snap)
        {
            var angle = AngleOf(at);
            var step = angle - lastAngle;

            step -= MathF.Tau * MathF.Round(step / MathF.Tau);
            lastAngle = angle;
            turned += step;

            if (typed.HasValue)
                return float.DegreesToRadians(typed.Value);

            var degrees = float.RadiansToDegrees(spec.Start - turned);

            if (snap.SnapsToIncrement && snap.Grid.AngleStep > 0f)
                degrees = MathF.Round(degrees / snap.Grid.AngleStep) * snap.Grid.AngleStep;

            return float.DegreesToRadians(degrees);
        }

        private float AngleOf(in Vector2 at)
        {
            var away = at - PivotScreen;

            return away.LengthSquared() < 1e-6f ? lastAngle : MathF.Atan2(away.Y, away.X);
        }

        private string AmountText(float amount) => spec.Unit switch
        {
            AmountUnit.Angle => $"　{float.RadiansToDegrees(amount):F1}°",
            AmountUnit.Count => string.Empty,
            AmountUnit.Factor => $"　{amount:F2}",
            _ => $"　{WorldScale.ToPixels(amount * scale):F1} px",
        };

        private string CountText()
        {
            var text = spec.CountLabel.Length > 0 ? $"　{spec.CountLabel} {count}" : string.Empty;

            if (spec.ShiftLabel.Length > 0)
                text += $"　{spec.ShiftLabel} {shift}";

            return text.Length > 0 ? $"{text}（ホイール{(spec.ShiftLabel.Length > 0 ? "・Ctrl+ホイール" : string.Empty)}で変える）" : string.Empty;
        }

        private string TypedText()
        {
            if (!typed.HasValue)
                return string.Empty;

            var unit = spec.Unit switch
            {
                AmountUnit.Angle => "°",
                AmountUnit.Count or AmountUnit.Factor => string.Empty,
                _ => " px",
            };

            return $"　入力 {typed.Text}{unit}";
        }

        private void End()
        {
            work = null;
            parameter = null;
            original = null;
            selection = null;
            made = null;
            typed.Clear();
            Status = string.Empty;
        }

        private static float Length(in Matrix4x4 toWorld, in Vector3 axis) => Vector3.TransformNormal(axis, toWorld).Length();
    }
}
