using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal sealed class MeshSnapper
    {
        private readonly List<Surface> surfaces = [];

        public MeshSnapper(MeshEditTarget edit, MeshSelection selection, IEnumerable<(MeshTopology Mesh, Matrix4x4 ToWorld)> others)
        {
            var mesh = edit.Mesh;
            var moving = new HashSet<int>(selection.Vertices);
            var skipped = new HashSet<int>(selection.HiddenFaces);

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (mesh.Faces[f].Any(moving.Contains))
                    skipped.Add(f);
            }

            var points = new List<int>();

            for (var i = 0; i < mesh.Points.Length; i++)
            {
                if (!moving.Contains(i) && selection.IsVertexShown(i))
                    points.Add(i);
            }

            var edges = new List<int>();

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                if (selection.IsEdgeShown(e) && !moving.Contains(mesh.Edges[e].A) && !moving.Contains(mesh.Edges[e].B))
                    edges.Add(e);
            }

            surfaces.Add(new Surface(mesh, edit.ToWorld, new MeshRaycaster(mesh, skipped), [.. points], [.. edges]));

            foreach (var (other, toWorld) in others)
            {
                surfaces.Add(new Surface(
                    other,
                    toWorld,
                    new MeshRaycaster(other),
                    [.. Enumerable.Range(0, other.Points.Length)],
                    [.. Enumerable.Range(0, other.Edges.Length)]));
            }
        }

        public SnapHit? Find(in Vector2 screen, in ViewSnapshot view, SnapTargets targets)
        {
            if ((targets & SnapTargets.Vertex) != 0 && NearestVertex(screen, view) is { } vertex)
                return vertex;

            if ((targets & SnapTargets.Edge) != 0 && NearestEdge(screen, view) is { } edge)
                return edge;

            if ((targets & SnapTargets.Face) != 0 && NearestFace(screen, view) is { } face)
                return face;

            return null;
        }

        private SnapHit? NearestVertex(in Vector2 screen, in ViewSnapshot view)
        {
            var nearest = MeshPicker.Reach;
            SnapHit? found = null;

            foreach (var surface in surfaces)
            {
                foreach (var index in surface.Points)
                {
                    var placed = Vector3.Transform(surface.Mesh.Points[index], surface.ToWorld);

                    if (view.ToScreen(placed) is not { } at)
                        continue;

                    var distance = Vector2.Distance(screen, at);

                    if (distance >= nearest)
                        continue;

                    nearest = distance;
                    found = new SnapHit(placed, SnapTargets.Vertex);
                }
            }

            return found;
        }

        private SnapHit? NearestEdge(in Vector2 screen, in ViewSnapshot view)
        {
            if (view.Ray(screen) is not { } ray)
                return null;

            var nearest = MeshPicker.Reach;
            SnapHit? found = null;

            foreach (var surface in surfaces)
            {
                foreach (var index in surface.Edges)
                {
                    var edge = surface.Mesh.Edges[index];
                    var a = Vector3.Transform(surface.Mesh.Points[edge.A], surface.ToWorld);
                    var b = Vector3.Transform(surface.Mesh.Points[edge.B], surface.ToWorld);

                    if (view.ToScreen(a) is not { } from || view.ToScreen(b) is not { } to)
                        continue;

                    var distance = ScreenRegion.DistanceToSegment(screen, from, to);

                    if (distance >= nearest)
                        continue;

                    nearest = distance;
                    found = new SnapHit(ClosestOnSegment(ray, a, b), SnapTargets.Edge);
                }
            }

            return found;
        }

        private SnapHit? NearestFace(in Vector2 screen, in ViewSnapshot view)
        {
            if (view.Ray(screen) is not { } ray)
                return null;

            var nearest = float.MaxValue;
            SnapHit? found = null;

            foreach (var surface in surfaces)
            {
                if (!Matrix4x4.Invert(surface.ToWorld, out var toLocal))
                    continue;

                var origin = Vector3.Transform(ray.Origin, toLocal);
                var direction = Vector3.TransformNormal(ray.Direction, toLocal);

                if (surface.Caster.Cast(origin, direction) is not { } hit)
                    continue;

                var placed = Vector3.Transform(origin + direction * hit.Distance, surface.ToWorld);
                var distance = Vector3.Distance(placed, ray.Origin);

                if (distance >= nearest)
                    continue;

                nearest = distance;
                found = new SnapHit(placed, SnapTargets.Face);
            }

            return found;
        }

        private static Vector3 ClosestOnSegment(in PickRay ray, in Vector3 a, in Vector3 b)
        {
            var along = b - a;
            var across = ray.Origin - a;
            var aa = Vector3.Dot(along, along);
            var ab = Vector3.Dot(along, ray.Direction);
            var bb = Vector3.Dot(ray.Direction, ray.Direction);
            var ac = Vector3.Dot(along, across);
            var bc = Vector3.Dot(ray.Direction, across);
            var denominator = aa * bb - ab * ab;

            var rate = MathF.Abs(denominator) < 1e-9f ? 0f : (ac * bb - ab * bc) / denominator;

            return a + along * Math.Clamp(rate, 0f, 1f);
        }

        private sealed record Surface(MeshTopology Mesh, Matrix4x4 ToWorld, MeshRaycaster Caster, int[] Points, int[] Edges);
    }
}
