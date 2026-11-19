using System.Numerics;
using System.Windows.Input;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics.Models;
using YMM43D.PreviewTool.Rendering;
using YMM43D.PreviewTool.Views;

namespace YMM43D.PreviewTool
{
    internal sealed class UvEditor
    {
        private const float PickReach = 10f;
        private const float FitRatio = 0.8f;
        private const float ZoomStep = 1.2f;
        private const float BoxSlop = 4f;
        private const float MinZoom = 8f;
        private const float MaxZoom = 100000f;
        private const float SameSpot = 1e-10f;

        private Vector2 pan = new(0.5f, 0.5f);
        private float zoom;
        private Vector2 size;
        private (Vector2 Mouse, Vector2 Pan)? panning;
        private Vector2? boxStart;
        private bool boxing;
        private Vector2 mouse;
        private UvMove? move;

        public event Action<Vector2[][]>? Committed;

        public bool IsOn { get; private set; }

        public bool IsMoving => move is not null;

        public string Status => move is { } moving ? moving.Status() : "UV エディタ：G 移動・R 回転・S 拡大縮小　ホイールで寄る・中ボタンで動かす　Home で全体";

        public void Toggle(bool on)
        {
            IsOn = on;
            move = null;
            boxStart = null;
            boxing = false;
            panning = null;
        }

        public void Fit()
        {
            pan = new Vector2(0.5f, 0.5f);
            zoom = MathF.Max(MathF.Min(size.X, size.Y) * FitRatio, MinZoom);
        }

        public UvSheet Sheet(MeshTopology mesh, MeshSelection selection, Vector2 view, ModelImage? image)
        {
            if (size != view)
            {
                size = view;

                if (zoom <= 0f)
                    Fit();
            }

            var uvs = move is { } moving ? Preview(moving) : mesh.UvsOrGenerated();
            var lines = new List<Vector2>();
            var seams = new List<Vector2>();
            var chosenLines = new List<Vector2>();
            var dots = new List<Vector2>();
            var chosenDots = new List<Vector2>();
            var chosenFaces = new List<Vector2[]>();
            var part = selection.Part;

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!selection.IsFaceShown(f))
                    continue;

                var corners = mesh.Faces[f];
                var screen = uvs[f].Select(ToScreen).ToArray();

                if (selection.Faces.Contains(f))
                    chosenFaces.Add(screen);

                for (var i = 0; i < corners.Length; i++)
                {
                    var next = (i + 1) % corners.Length;
                    var edge = mesh.FaceEdges[f][i];
                    var into = selection.Edges.Contains(edge) ? chosenLines : mesh.MarkOf(edge).Seam ? seams : lines;

                    into.Add(screen[i]);
                    into.Add(screen[next]);

                    if (part is MeshPart.Vertices)
                        (selection.Vertices.Contains(corners[i]) ? chosenDots : dots).Add(screen[i]);
                }
            }

            (Vector2, Vector2)? box = boxing && boxStart is { } from ? (from, mouse) : null;

            return new UvSheet(image, ToScreen(Vector2.Zero), zoom, [.. lines], [.. seams], [.. chosenLines], [.. dots], [.. chosenDots], [.. chosenFaces], box);
        }

        public void Mouse(D3D11Host.MouseEventKind kind, Vector2 at, int delta, ModifierKeys modifiers, MeshTopology mesh, MeshSelection selection)
        {
            if (kind is not D3D11Host.MouseEventKind.Wheel)
                mouse = at;

            switch (kind)
            {
                case D3D11Host.MouseEventKind.Wheel:
                    ZoomAt(at, delta > 0 ? ZoomStep : 1f / ZoomStep);
                    return;

                case D3D11Host.MouseEventKind.MiddleDown:
                    panning = (at, pan);
                    return;

                case D3D11Host.MouseEventKind.MiddleUp:
                    panning = null;
                    return;

                case D3D11Host.MouseEventKind.Move when panning is { } held:
                    pan = held.Pan - (at - held.Mouse) / zoom;
                    return;

                case D3D11Host.MouseEventKind.Down when move is not null:
                    Finish(true);
                    return;

                case D3D11Host.MouseEventKind.RightDown when move is not null:
                    Finish(false);
                    return;

                case D3D11Host.MouseEventKind.Down:
                    boxStart = at;
                    boxing = false;
                    return;

                case D3D11Host.MouseEventKind.Move when boxStart is { } start:
                    if (!boxing && Vector2.Distance(start, at) > BoxSlop)
                        boxing = true;

                    return;

                case D3D11Host.MouseEventKind.Up when boxStart is { } start:
                    if (boxing)
                        SelectBox(mesh, selection, start, at, modifiers);
                    else
                        SelectAt(mesh, selection, at, modifiers);

                    boxStart = null;
                    boxing = false;
                    return;
            }
        }

        public bool Press(Key key, ModifierKeys modifiers, MeshTopology mesh, MeshSelection selection)
        {
            if (move is { } moving)
            {
                switch (key)
                {
                    case Key.Enter or Key.Space:
                        Finish(true);
                        break;

                    case Key.Escape:
                        Finish(false);
                        break;

                    case Key.X:
                        moving.Axis = moving.Axis is UvAxis.U ? UvAxis.Both : UvAxis.U;
                        break;

                    case Key.Y:
                        moving.Axis = moving.Axis is UvAxis.V ? UvAxis.Both : UvAxis.V;
                        break;
                }

                return true;
            }

            if (modifiers != ModifierKeys.None)
                return false;

            switch (key)
            {
                case Key.G:
                    return Begin(UvMoveKind.Move, mesh, selection);

                case Key.R:
                    return Begin(UvMoveKind.Rotate, mesh, selection);

                case Key.S:
                    return Begin(UvMoveKind.Scale, mesh, selection);

                case Key.Home:
                    Fit();
                    return true;

                default:
                    return false;
            }
        }

        private Vector2 ToScreen(Vector2 uv) => size / 2f + (uv - pan) * zoom;

        private Vector2 ToUv(Vector2 screen) => pan + (screen - size / 2f) / zoom;

        private void ZoomAt(Vector2 at, float factor)
        {
            var fixedUv = ToUv(at);

            zoom = Math.Clamp(zoom * factor, MinZoom, MaxZoom);
            pan = fixedUv - (at - size / 2f) / zoom;
        }

        public static Vector2? Center(MeshTopology mesh, MeshSelection selection)
        {
            var source = mesh.UvsOrGenerated();
            var corners = Corners(mesh, selection, source);

            return corners.Count == 0 ? null : Middle(source, corners);
        }

        public static Vector2[][]? MovedTo(MeshTopology mesh, MeshSelection selection, Vector2 target)
        {
            var source = mesh.UvsOrGenerated();
            var corners = Corners(mesh, selection, source);

            if (corners.Count == 0)
                return null;

            var shift = target - Middle(source, corners);
            var result = source.Select(face => (Vector2[])face.Clone()).ToArray();

            foreach (var (face, corner) in corners)
                result[face][corner] += shift;

            return result;
        }

        private static Vector2 Middle(Vector2[][] source, List<(int Face, int Corner)> corners)
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var (face, corner) in corners)
            {
                min = Vector2.Min(min, source[face][corner]);
                max = Vector2.Max(max, source[face][corner]);
            }

            return (min + max) / 2f;
        }

        private bool Begin(UvMoveKind kind, MeshTopology mesh, MeshSelection selection)
        {
            var source = mesh.UvsOrGenerated();
            var corners = Corners(mesh, selection, source);

            if (corners.Count == 0)
                return true;

            move = new UvMove(kind, source, [.. corners], Middle(source, corners), mouse);
            boxStart = null;
            boxing = false;

            return true;
        }

        private static List<(int Face, int Corner)> Corners(MeshTopology mesh, MeshSelection selection, Vector2[][] source)
        {
            var candidates = new List<(int Face, int Corner)>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!selection.IsFaceShown(f))
                    continue;

                for (var i = 0; i < mesh.Faces[f].Length; i++)
                {
                    if (selection.Vertices.Contains(mesh.Faces[f][i]))
                        candidates.Add((f, i));
                }
            }

            var corners = candidates;

            if (selection.Part is MeshPart.Faces)
            {
                var spots = candidates
                    .Where(one => selection.Faces.Contains(one.Face))
                    .ToLookup(one => mesh.Faces[one.Face][one.Corner], one => source[one.Face][one.Corner]);

                corners = [.. candidates.Where(one => spots[mesh.Faces[one.Face][one.Corner]].Any(spot => Vector2.DistanceSquared(spot, source[one.Face][one.Corner]) < SameSpot))];
            }

            return corners;
        }

        private Vector2[][] Preview(UvMove moving)
        {
            var result = moving.Source.Select(face => (Vector2[])face.Clone()).ToArray();
            var pivotScreen = ToScreen(moving.Pivot);
            var from = moving.Start - pivotScreen;
            var to = mouse - pivotScreen;
            var angle = MathF.Atan2(to.Y, to.X) - MathF.Atan2(from.Y, from.X);
            var amount = moving.Kind switch
            {
                UvMoveKind.Move => Lock((mouse - moving.Start) / zoom, moving.Axis, Vector2.Zero),
                UvMoveKind.Rotate => new Vector2(float.RadiansToDegrees(angle), 0f),
                _ => Lock(new Vector2(to.Length() / MathF.Max(from.Length(), 1f)), moving.Axis, Vector2.One),
            };
            var turn = Matrix3x2.CreateRotation(angle);

            foreach (var (face, corner) in moving.Corners)
            {
                var uv = moving.Source[face][corner];

                result[face][corner] = moving.Kind switch
                {
                    UvMoveKind.Move => uv + amount,
                    UvMoveKind.Rotate => moving.Pivot + Vector2.Transform(uv - moving.Pivot, turn),
                    _ => moving.Pivot + (uv - moving.Pivot) * amount,
                };
            }

            moving.Last = amount;

            return result;
        }

        private static Vector2 Lock(Vector2 value, UvAxis axis, Vector2 rest) => axis switch
        {
            UvAxis.U => new Vector2(value.X, rest.Y),
            UvAxis.V => new Vector2(rest.X, value.Y),
            _ => value,
        };

        private void Finish(bool apply)
        {
            if (move is not { } moving)
                return;

            var result = Preview(moving);

            move = null;

            if (apply)
                Committed?.Invoke(result);
        }

        private void SelectAt(MeshTopology mesh, MeshSelection selection, Vector2 at, ModifierKeys modifiers)
        {
            var uvs = mesh.UvsOrGenerated();
            var best = PickReach;
            var hit = -1;

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!selection.IsFaceShown(f))
                    continue;

                var corners = mesh.Faces[f];
                var screen = uvs[f].Select(ToScreen).ToArray();

                switch (selection.Part)
                {
                    case MeshPart.Vertices:
                        for (var i = 0; i < corners.Length; i++)
                        {
                            var distance = Vector2.Distance(screen[i], at);

                            if (distance < best)
                                (best, hit) = (distance, corners[i]);
                        }

                        break;

                    case MeshPart.Edges:
                        for (var i = 0; i < corners.Length; i++)
                        {
                            var distance = SegmentDistance(at, screen[i], screen[(i + 1) % corners.Length]);

                            if (distance < best)
                                (best, hit) = (distance, mesh.FaceEdges[f][i]);
                        }

                        break;

                    default:
                        if (Inside(screen, at))
                            hit = f;

                        break;
                }
            }

            var op = (modifiers & ModifierKeys.Shift) != 0 ? SelectOp.Toggle : (modifiers & ModifierKeys.Control) != 0 ? SelectOp.Subtract : SelectOp.Set;

            if (hit >= 0)
                selection.Apply([hit], op, hit);
            else if (op is SelectOp.Set)
                selection.Apply([], SelectOp.Set);
        }

        private void SelectBox(MeshTopology mesh, MeshSelection selection, Vector2 from, Vector2 to, ModifierKeys modifiers)
        {
            var min = Vector2.Min(from, to);
            var max = Vector2.Max(from, to);
            var uvs = mesh.UvsOrGenerated();
            var found = new HashSet<int>();

            bool Within(Vector2 point) => point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!selection.IsFaceShown(f))
                    continue;

                var corners = mesh.Faces[f];
                var screen = uvs[f].Select(ToScreen).ToArray();

                switch (selection.Part)
                {
                    case MeshPart.Vertices:
                        for (var i = 0; i < corners.Length; i++)
                        {
                            if (Within(screen[i]))
                                found.Add(corners[i]);
                        }

                        break;

                    case MeshPart.Edges:
                        for (var i = 0; i < corners.Length; i++)
                        {
                            if (Within(screen[i]) && Within(screen[(i + 1) % corners.Length]))
                                found.Add(mesh.FaceEdges[f][i]);
                        }

                        break;

                    default:
                        if (Within(screen.Aggregate(Vector2.Zero, (sum, point) => sum + point) / screen.Length))
                            found.Add(f);

                        break;
                }
            }

            var op = (modifiers & ModifierKeys.Shift) != 0 ? SelectOp.Add : (modifiers & ModifierKeys.Control) != 0 ? SelectOp.Subtract : SelectOp.Set;

            selection.Apply(found, op);
        }

        private static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            var span = b - a;
            var length = span.LengthSquared();
            var t = length < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(point - a, span) / length, 0f, 1f);

            return Vector2.Distance(point, a + span * t);
        }

        private static bool Inside(Vector2[] polygon, Vector2 point)
        {
            var inside = false;

            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                    && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private enum UvMoveKind { Move, Rotate, Scale }

        private enum UvAxis { Both, U, V }

        private sealed class UvMove(UvMoveKind kind, Vector2[][] source, (int Face, int Corner)[] corners, Vector2 pivot, Vector2 start)
        {
            public UvMoveKind Kind { get; } = kind;

            public Vector2[][] Source { get; } = source;

            public (int Face, int Corner)[] Corners { get; } = corners;

            public Vector2 Pivot { get; } = pivot;

            public Vector2 Start { get; } = start;

            public UvAxis Axis { get; set; }

            public Vector2 Last { get; set; }

            public string Status()
            {
                var axis = Axis switch
                {
                    UvAxis.U => "　U だけ",
                    UvAxis.V => "　V だけ",
                    _ => string.Empty,
                };

                var value = Kind switch
                {
                    UvMoveKind.Move => $"UV 移動　U {Last.X:F3}　V {Last.Y:F3}",
                    UvMoveKind.Rotate => $"UV 回転　{Last.X:F1}°",
                    _ => $"UV 拡大縮小　U {Last.X:F3}　V {Last.Y:F3}",
                };

                return $"{value}{axis}　X/Y で軸をしぼる・クリックか Enter で決める・右クリックか Esc でやめる";
            }
        }
    }
}
