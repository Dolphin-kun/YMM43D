using System.Numerics;
using System.Windows.Input;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;

namespace YMM43D.PreviewTool
{
    internal sealed class ModalTransform
    {
        private const float RadiusStep = 1.15f;

        private const int CustomAxis = 3;

        private static readonly string[] AxisNames = ["X", "Y", "Z"];

        private readonly TypedNumber typed = new();

        private Shape3DParameter? parameter;
        private MeshTopology? original;
        private MeshTransformer? transformer;
        private MeshSnapper? snapper;
        private MeshSelection? selection;
        private Matrix4x4 toWorld;
        private Matrix4x4 facing = Matrix4x4.Identity;
        private Vector2 startMouse;
        private Vector2 mouse;
        private float turned;
        private float lastAngle;
        private int axis = -1;
        private bool plane;
        private bool local;
        private Vector3 custom;
        private SnapTargets snapped;

        public bool IsActive => parameter is not null;

        public TransformKind Kind { get; private set; }

        public TransformKind Requested { get; private set; }

        public Vector3 Pivot => transformer?.Pivot ?? Vector3.Zero;

        public Vector2 PivotScreen { get; private set; }

        public Vector2 Mouse => mouse;

        public string Status { get; private set; } = string.Empty;

        public ProportionalEdit? Proportional => transformer?.Proportional;

        public bool Begin(
            TransformKind kind,
            in MeshEditTarget edit,
            MeshSelection chosen,
            PivotPoint pivot,
            in Vector3 cursor,
            ProportionalEdit? proportional,
            MeshSnapper? snaps,
            in Vector2 at,
            in ViewSnapshot view)
        {
            if (chosen.IsEmpty || edit.Item.IsLocked)
                return false;

            var mesh = edit.Parameter.EnsureMesh();

            chosen.Bind(mesh);

            if (chosen.IsEmpty)
                return false;

            parameter = edit.Parameter;
            original = mesh;
            selection = chosen;
            snapper = snaps;
            toWorld = edit.ToWorld;
            facing = Facing(edit.ToWorld);
            transformer = new MeshTransformer(mesh, edit.ToWorld, chosen, pivot, cursor, proportional);
            Kind = kind;
            startMouse = mouse = at;
            PivotScreen = view.ToScreen(transformer.Pivot) ?? at;
            turned = 0f;
            lastAngle = AngleOf(at);
            axis = -1;
            plane = false;
            local = false;
            snapped = SnapTargets.None;
            typed.Clear();

            return true;
        }

        public void Update(in Vector2 at, in ViewSnapshot view, in SnapSettings snap)
        {
            if (transformer is null || original is null || parameter is null)
                return;

            mouse = at;
            snapped = SnapTargets.None;

            var points = Kind switch
            {
                TransformKind.Rotate => Rotate(view, snap),
                TransformKind.Scale => Scale(snap),
                _ => Move(view, snap),
            };

            parameter.StorePoints(original.WithPoints(points));
        }

        public ModalResult Press(Key key, ModifierKeys modifiers)
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

                case Key.X or Key.Y or Key.Z:
                    Lock(key - Key.X, (modifiers & ModifierKeys.Shift) != 0);
                    return ModalResult.Handled;

                case Key.G or Key.R or Key.S:
                    Requested = key switch
                    {
                        Key.R => TransformKind.Rotate,
                        Key.S => TransformKind.Scale,
                        _ => TransformKind.Move,
                    };

                    return Requested == Kind ? ModalResult.Handled : ModalResult.Restart;

                case Key.O:
                    return ModalResult.ToggleProportional;

                case Key.PageUp:
                    Widen(RadiusStep);
                    return ModalResult.Handled;

                case Key.PageDown:
                    Widen(1f / RadiusStep);
                    return ModalResult.Handled;

                default:
                    typed.Take(key);
                    return ModalResult.Handled;
            }
        }

        public void LockTo(in Vector3 direction)
        {
            if (direction.LengthSquared() < 1e-12f)
                return;

            custom = Vector3.Normalize(direction);
            axis = CustomAxis;
            plane = false;
            local = false;
        }

        public void Widen(float ratio)
        {
            if (transformer?.Proportional is { } shaped)
                transformer.Proportional = shaped with { Radius = MathF.Max(shaped.Radius * ratio, ProportionalEdit.LeastRadius) };
        }

        public void SetProportional(ProportionalEdit? proportional)
        {
            transformer?.Proportional = proportional;
        }

        public void Confirm() => End();

        public void Cancel()
        {
            if (parameter is not null && original is not null)
                parameter.StorePoints(original);

            End();
        }

        public IEnumerable<(Vector3 Direction, int Axis)> Guides()
        {
            if (!IsActive || axis < 0)
                yield break;

            if (!plane || Kind is TransformKind.Rotate || axis == CustomAxis)
            {
                yield return (AxisOf(axis), axis);
                yield break;
            }

            for (var i = 0; i < 3; i++)
            {
                if (i != axis)
                    yield return (AxisOf(i), i);
            }
        }

        private void End()
        {
            parameter = null;
            original = null;
            transformer = null;
            snapper = null;
            selection = null;
            typed.Clear();
            Status = string.Empty;
        }

        private void Lock(int index, bool asPlane)
        {
            if (axis == CustomAxis)
                axis = -1;

            if (axis == index && plane == asPlane)
            {
                if (!local)
                {
                    local = true;
                }
                else
                {
                    axis = -1;
                    local = false;
                }

                return;
            }

            axis = index;
            plane = asPlane;
            local = false;
        }

        private Vector3[] Move(in ViewSnapshot view, in SnapSettings snap)
        {
            var shift = Shift(view, snap);
            var pixels = WorldScale.ToPixelOffset(shift);

            Status = $"移動　X {pixels.X:F1}　Y {pixels.Y:F1}　Z {pixels.Z:F1} px{LockText()}{TypedText(" px")}{SnapText()}{RadiusText()}";

            return transformer!.Translate(shift);
        }

        private Vector3 Shift(in ViewSnapshot view, in SnapSettings snap)
        {
            var pivot = transformer!.Pivot;

            if (typed.HasValue)
            {
                var along = axis >= 0 && !plane ? axis : axis >= 0 ? (axis + 1) % 3 : 0;
                var sign = !local && along == 1 ? -1f : 1f;

                return AxisOf(along) * WorldScale.ToWorld(typed.Value) * sign;
            }

            var shift = Vector3.Zero;

            if (view.Ray(startMouse) is { } from && view.Ray(mouse) is { } to)
            {
                if (axis >= 0 && !plane)
                {
                    var direction = AxisOf(axis);

                    if (TransformGizmo.ClosestOnAxis(from, pivot, direction) is { } start
                        && TransformGizmo.ClosestOnAxis(to, pivot, direction) is { } end)
                    {
                        shift = direction * (end - start);
                    }
                }
                else
                {
                    var normal = axis >= 0 ? AxisOf(axis) : -view.Forward;

                    if (from.IntersectPlane(pivot, normal) is { } start && to.IntersectPlane(pivot, normal) is { } end)
                        shift = end - start;
                }
            }

            if (snap.SnapsToElements && snapper?.Find(mouse, view, snap.Targets) is { } hit)
            {
                snapped = hit.Kind;

                return Constrain(hit.Point - BaseFor(snap.Base, hit.Point));
            }

            return snap.SnapsToIncrement ? Constrain(snap.Grid.SnapShift(pivot, shift)) : shift;
        }

        private Vector3 Constrain(in Vector3 shift)
        {
            if (axis < 0)
                return shift;

            var direction = AxisOf(axis);
            var along = direction * Vector3.Dot(shift, direction);

            return plane ? shift - along : along;
        }

        private Vector3 BaseFor(SnapBase wanted, in Vector3 target)
        {
            var pivot = transformer!.Pivot;

            if (selection is null || original is null)
                return pivot;

            switch (wanted)
            {
                case SnapBase.Active:
                    return MeshPivot.ActiveCenter(original, toWorld, selection) ?? pivot;

                case SnapBase.Closest:
                    var nearest = float.MaxValue;
                    var found = pivot;

                    foreach (var vertex in selection.Vertices)
                    {
                        var placed = Vector3.Transform(original.Points[vertex], toWorld);
                        var distance = Vector3.DistanceSquared(placed, target);

                        if (distance >= nearest)
                            continue;

                        nearest = distance;
                        found = placed;
                    }

                    return found;

                default:
                    return pivot;
            }
        }

        private Vector3[] Rotate(in ViewSnapshot view, in SnapSettings snap)
        {
            var angle = AngleOf(mouse);
            var step = angle - lastAngle;

            step -= MathF.Tau * MathF.Round(step / MathF.Tau);
            lastAngle = angle;
            turned += step;

            var toward = -view.Forward;
            var direction = axis >= 0 ? AxisOf(axis) : toward;
            var sign = Vector3.Dot(direction, toward) >= 0f ? -1f : 1f;
            var degrees = typed.HasValue ? typed.Value : float.RadiansToDegrees(turned * sign);

            if (!typed.HasValue && snap.SnapsToIncrement && snap.Grid.AngleStep > 0f)
                degrees = MathF.Round(degrees / snap.Grid.AngleStep) * snap.Grid.AngleStep;

            Status = $"回転　{degrees:F1}°{LockText()}{TypedText("°")}{RadiusText()}";

            return transformer!.Rotate(direction, float.DegreesToRadians(degrees));
        }

        private Vector3[] Scale(in SnapSettings snap)
        {
            var start = MathF.Max(Vector2.Distance(startMouse, PivotScreen), 1f);
            var ratio = typed.HasValue ? typed.Value : Vector2.Distance(mouse, PivotScreen) / start;

            if (!typed.HasValue && snap.SnapsToIncrement)
                ratio = MathF.Round(ratio * 10f) / 10f;

            var factors = new Vector3(ratio);

            if (axis >= 0)
            {
                factors = plane ? new Vector3(ratio) : Vector3.One;

                SetComponent(ref factors, axis, plane ? 1f : ratio);
            }

            Status = $"拡大　{ratio:F3} 倍{LockText()}{TypedText(" 倍")}{RadiusText()}";

            return transformer!.Scale(factors, local ? facing : Matrix4x4.Identity);
        }

        private static void SetComponent(ref Vector3 value, int index, float component)
        {
            switch (index)
            {
                case 0:
                    value.X = component;
                    break;

                case 1:
                    value.Y = component;
                    break;

                default:
                    value.Z = component;
                    break;
            }
        }

        private Vector3 AxisOf(int index)
        {
            if (index == CustomAxis)
                return custom;

            var unit = index switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                _ => Vector3.UnitZ,
            };

            return local ? Vector3.Normalize(Vector3.TransformNormal(unit, facing)) : unit;
        }

        private float AngleOf(in Vector2 at)
        {
            var away = at - PivotScreen;

            return away.LengthSquared() < 1e-6f ? lastAngle : MathF.Atan2(away.Y, away.X);
        }

        private string LockText()
        {
            if (axis < 0)
                return string.Empty;

            if (axis == CustomAxis)
                return "（法線の向き）";

            var space = local ? "ローカル" : "グローバル";

            return plane && Kind is not TransformKind.Rotate
                ? $"（{AxisNames[axis]} 以外の面・{space}）"
                : $"（{AxisNames[axis]} 軸・{space}）";
        }

        private string TypedText(string unit) => typed.HasValue ? $"　入力 {typed.Text}{unit}" : string.Empty;

        private string SnapText() => snapped switch
        {
            SnapTargets.Vertex => "　頂点にスナップ",
            SnapTargets.Edge => "　辺にスナップ",
            SnapTargets.Face => "　面にスナップ",
            _ => string.Empty,
        };

        private string RadiusText()
            => transformer?.Proportional is { } shaped ? $"　影響範囲 {WorldScale.ToPixels(shaped.Radius):F0} px" : string.Empty;

        private static Matrix4x4 Facing(in Matrix4x4 toWorld)
        {
            var x = Direction(new Vector3(toWorld.M11, toWorld.M12, toWorld.M13), Vector3.UnitX);
            var y = Direction(new Vector3(toWorld.M21, toWorld.M22, toWorld.M23), Vector3.UnitY);
            var z = Direction(new Vector3(toWorld.M31, toWorld.M32, toWorld.M33), Vector3.UnitZ);

            return new Matrix4x4(
                x.X, x.Y, x.Z, 0f,
                y.X, y.Y, y.Z, 0f,
                z.X, z.Y, z.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        private static Vector3 Direction(in Vector3 axis, in Vector3 fallback) => axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : fallback;
    }
}
