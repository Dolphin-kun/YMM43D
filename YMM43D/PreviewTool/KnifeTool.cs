using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal sealed class KnifeTool
    {
        private const float Edge = 1e-4f;

        private readonly List<List<KnifeStep>> paths = [];

        public bool IsActive { get; private set; }

        public bool CutsThrough { get; private set; }

        public Vector2 Mouse { get; set; }

        public string Status => $"ナイフ　クリックで点を打つ　Enter で切る　E で新しい線　Z で裏まで{(CutsThrough ? "（入）" : "（切）")}　Backspace で 1 点戻す　Esc でやめる";

        public void Begin(Vector2 at)
        {
            paths.Clear();
            paths.Add([]);
            Mouse = at;
            CutsThrough = false;
            IsActive = true;
        }

        public void End()
        {
            paths.Clear();
            IsActive = false;
        }

        public void ToggleThrough() => CutsThrough = !CutsThrough;

        public void NewLine()
        {
            if (paths[^1].Count > 0)
                paths.Add([]);
        }

        public void Undo()
        {
            if (paths[^1].Count > 0)
                paths[^1].RemoveAt(paths[^1].Count - 1);
            else if (paths.Count > 1)
                paths.RemoveAt(paths.Count - 1);
        }

        public void Add(MeshPicker pick, MeshSelection selection, Vector2 screen)
        {
            var hit = Snap(pick, screen);
            var edit = pick.Edit;
            Vector3? world = hit is { } found
                ? Vector3.Transform(found.Place, edit.ToWorld)
                : pick.View.Ray(screen)?.IntersectPlane(Vector3.Transform(Vector3.Zero, edit.ToWorld), -pick.View.Forward);

            if (world is not { } placed || pick.View.ToScreen(placed) is not { } to)
                return;

            var path = paths[^1];
            List<KnifePoint> crossings = path.Count > 0 && pick.View.ToScreen(path[^1].World) is { } from ? [.. Crossings(pick, selection, from, to)] : [];

            path.Add(new KnifeStep(placed, hit, crossings));
        }

        public List<IReadOnlyList<KnifePoint>> Cuts()
            => [.. paths.Where(path => path.Count >= 2).Select(path => (IReadOnlyList<KnifePoint>)[.. path.SelectMany(Events)])];

        public IEnumerable<IReadOnlyList<Vector2>> Lines(in ViewSnapshot view)
        {
            var lines = new List<IReadOnlyList<Vector2>>();

            for (var i = 0; i < paths.Count; i++)
            {
                var line = new List<Vector2>();

                foreach (var step in paths[i])
                {
                    if (view.ToScreen(step.World) is { } at)
                        line.Add(at);
                }

                if (i == paths.Count - 1 && line.Count > 0)
                    line.Add(Mouse);

                if (line.Count >= 2)
                    lines.Add(line);
            }

            return lines;
        }

        public IEnumerable<Vector2> Marks(MeshPicker pick, MeshSelection selection)
        {
            var shown = paths.SelectMany(path => path.SelectMany(Events)).ToList();

            if (paths[^1] is { Count: > 0 } last && pick.View.ToScreen(last[^1].World) is { } from)
                shown.AddRange(Crossings(pick, selection, from, Mouse));

            foreach (var one in shown)
            {
                if (pick.View.ToScreen(Vector3.Transform(one.Place, pick.Edit.ToWorld)) is { } at)
                    yield return at;
            }
        }

        private static IEnumerable<KnifePoint> Events(KnifeStep step)
            => step.Hit is { } hit ? [.. step.Crossings, hit] : step.Crossings;

        private IEnumerable<KnifePoint> Crossings(MeshPicker pick, MeshSelection selection, Vector2 from, Vector2 to)
        {
            var view = pick.View;
            var edit = pick.Edit;

            if (view.Ray(from) is not { } first || view.Ray(to) is not { } second || Vector2.DistanceSquared(from, to) < 1f)
                yield break;

            var origin = first.Origin;
            var normal = Vector3.Cross(first.Direction, second.Origin + second.Direction - origin);

            if (normal.LengthSquared() < 1e-12f)
                yield break;

            normal = Vector3.Normalize(normal);

            var span = to - from;
            var crossings = new List<(float Along, KnifePoint Point)>();
            var mesh = edit.Mesh;

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                if (!selection.IsEdgeShown(e))
                    continue;

                var (a, b) = (mesh.Edges[e].A, mesh.Edges[e].B);
                var (sa, sb) = (Vector3.Dot(edit.Points[a] - origin, normal), Vector3.Dot(edit.Points[b] - origin, normal));

                if (sa * sb > 0f || MathF.Abs(sa - sb) < 1e-12f)
                    continue;

                var t = Math.Clamp(sa / (sa - sb), 0f, 1f);

                if (view.ToScreen(Vector3.Lerp(edit.Points[a], edit.Points[b], t)) is not { } at)
                    continue;

                var along = Vector2.Dot(at - from, span) / span.LengthSquared();

                if (along is <= Edge or >= 1f - Edge)
                    continue;

                var local = Vector3.Lerp(mesh.Points[a], mesh.Points[b], t);

                if (!CutsThrough && !pick.Sees(local))
                    continue;

                crossings.Add((along, new KnifePoint(KnifeHit.Edge, e, local)));
            }

            foreach (var (_, point) in crossings.OrderBy(one => one.Along))
                yield return point;
        }

        private KnifePoint? Snap(MeshPicker pick, Vector2 screen)
        {
            var xray = CutsThrough;
            var mesh = pick.Edit.Mesh;
            var vertex = pick.Vertex(screen, xray);

            if (vertex >= 0)
                return new KnifePoint(KnifeHit.Vertex, vertex, mesh.Points[vertex]);

            var edge = pick.Edge(screen, xray);

            if (edge >= 0 && pick.View.Ray(screen) is { } ray && Matrix4x4.Invert(pick.Edit.ToWorld, out var toLocal))
            {
                var origin = Vector3.Transform(ray.Origin, toLocal);
                var direction = Vector3.TransformNormal(ray.Direction, toLocal);
                var (a, b) = (mesh.Points[mesh.Edges[edge].A], mesh.Points[mesh.Edges[edge].B]);

                return new KnifePoint(KnifeHit.Edge, edge, Vector3.Lerp(a, b, Closest(origin, direction, a, b - a)));
            }

            var face = pick.Face(screen, false);

            if (face >= 0 && pick.Surface(screen) is { } world && Matrix4x4.Invert(pick.Edit.ToWorld, out var back))
                return new KnifePoint(KnifeHit.Face, face, Vector3.Transform(world, back));

            return null;
        }

        private static float Closest(Vector3 origin, Vector3 direction, Vector3 start, Vector3 span)
        {
            var w = start - origin;
            var (a, b, c) = (Vector3.Dot(direction, direction), Vector3.Dot(direction, span), Vector3.Dot(span, span));
            var (d, e) = (Vector3.Dot(direction, w), Vector3.Dot(span, w));
            var below = a * c - b * b;

            return below < 1e-12f ? 0f : Math.Clamp((b * d - a * e) / below, 0f, 1f);
        }
    }
}
