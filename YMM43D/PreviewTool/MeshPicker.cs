using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal sealed class MeshPicker(MeshEditTarget edit, MeshSelection selection, MeshRaycaster caster, ViewSnapshot view)
    {
        public const float Reach = 10f;

        private readonly Matrix4x4 toLocal = Matrix4x4.Invert(edit.ToWorld, out var inverse) ? inverse : Matrix4x4.Identity;

        public MeshEditTarget Edit => edit;

        public ViewSnapshot View => view;

        public MeshRaycaster Caster => caster;

        public bool Sees(in Vector3 local)
        {
            if (view.Orthographic)
            {
                var toward = Vector3.TransformNormal(-view.Forward, toLocal);

                if (toward.LengthSquared() < 1e-12f)
                    return true;

                return !caster.IsBlocked(local, local + Vector3.Normalize(toward) * 1e6f);
            }

            return !caster.IsBlocked(local, Vector3.Transform(view.Eye, toLocal));
        }

        public int Vertex(in Vector2 screen, bool xray)
        {
            var nearest = Reach;
            var found = -1;

            for (var i = 0; i < edit.Points.Length; i++)
            {
                if (!selection.IsVertexShown(i) || view.ToScreen(edit.Points[i]) is not { } at)
                    continue;

                var distance = Vector2.Distance(screen, at);

                if (distance >= nearest || (!xray && !Sees(edit.Mesh.Points[i])))
                    continue;

                nearest = distance;
                found = i;
            }

            return found;
        }

        public int Edge(in Vector2 screen, bool xray)
        {
            var nearest = Reach;
            var found = -1;
            var mesh = edit.Mesh;

            for (var i = 0; i < mesh.Edges.Length; i++)
            {
                if (!selection.IsEdgeShown(i))
                    continue;

                var edge = mesh.Edges[i];

                if (view.ToScreen(edit.Points[edge.A]) is not { } from
                    || view.ToScreen(edit.Points[edge.B]) is not { } to)
                {
                    continue;
                }

                var span = to - from;
                var rate = span.LengthSquared() < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(screen - from, span) / span.LengthSquared(), 0f, 1f);
                var distance = Vector2.Distance(screen, from + span * rate);

                if (distance >= nearest)
                    continue;

                if (!xray && !Sees(Vector3.Lerp(mesh.Points[edge.A], mesh.Points[edge.B], rate)))
                    continue;

                nearest = distance;
                found = i;
            }

            return found;
        }

        public int Face(in Vector2 screen, bool xray)
        {
            if (view.Ray(screen) is not { } ray)
                return -1;

            if (!xray)
            {
                var origin = Vector3.Transform(ray.Origin, toLocal);
                var direction = Vector3.TransformNormal(ray.Direction, toLocal);

                return caster.Cast(origin, direction) is { } hit ? hit.Face : -1;
            }

            var mesh = edit.Mesh;
            var nearest = float.MaxValue;
            var found = -1;

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!selection.IsFaceShown(f)
                    || Flat(mesh.Faces[f]) is not { } flat
                    || !ScreenRegion.Lasso(flat).Contains(screen))
                {
                    continue;
                }

                var depth = Vector3.Distance(Center(f), view.Eye);

                if (depth >= nearest)
                    continue;

                nearest = depth;
                found = f;
            }

            return found;
        }

        public Vector3? Surface(in Vector2 screen)
        {
            if (view.Ray(screen) is not { } ray)
                return null;

            var origin = Vector3.Transform(ray.Origin, toLocal);
            var direction = Vector3.TransformNormal(ray.Direction, toLocal);

            return caster.Cast(origin, direction) is { } hit ? Vector3.Transform(origin + direction * hit.Distance, edit.ToWorld) : null;
        }

        public int[] Region(MeshPart part, ScreenRegion region, bool xray) => part switch
        {
            MeshPart.Faces => RegionFaces(region, xray),
            MeshPart.Edges => RegionEdges(region, xray),
            _ => RegionVertices(region, xray),
        };

        private int[] RegionVertices(ScreenRegion region, bool xray)
        {
            var found = new List<int>();

            for (var i = 0; i < edit.Points.Length; i++)
            {
                if (selection.IsVertexShown(i)
                    && view.ToScreen(edit.Points[i]) is { } at
                    && region.Contains(at)
                    && (xray || Sees(edit.Mesh.Points[i])))
                {
                    found.Add(i);
                }
            }

            return [.. found];
        }

        private int[] RegionEdges(ScreenRegion region, bool xray)
        {
            var mesh = edit.Mesh;
            var inside = new List<int>();
            var touching = new List<int>();

            for (var i = 0; i < mesh.Edges.Length; i++)
            {
                if (!selection.IsEdgeShown(i))
                    continue;

                var edge = mesh.Edges[i];

                if (view.ToScreen(edit.Points[edge.A]) is not { } from
                    || view.ToScreen(edit.Points[edge.B]) is not { } to)
                {
                    continue;
                }

                var a = mesh.Points[edge.A];
                var b = mesh.Points[edge.B];

                if (region.Contains(from) && region.Contains(to))
                {
                    if (xray || Sees(a) || Sees(b) || Sees((a + b) / 2f))
                        inside.Add(i);

                    continue;
                }

                if (region.Crosses(from, to) && (xray || Sees((a + b) / 2f)))
                    touching.Add(i);
            }

            return inside.Count > 0 ? [.. inside] : [.. touching];
        }

        private int[] RegionFaces(ScreenRegion region, bool xray)
        {
            var mesh = edit.Mesh;
            var found = new List<int>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!selection.IsFaceShown(f))
                    continue;

                var center = Center(f);

                if (view.ToScreen(center) is not { } at || !region.Contains(at))
                    continue;

                if (xray || Sees(LocalCenter(f)))
                    found.Add(f);
            }

            return [.. found];
        }

        private Vector2[]? Flat(int[] corners)
        {
            var flat = new Vector2[corners.Length];

            for (var i = 0; i < corners.Length; i++)
            {
                if (view.ToScreen(edit.Points[corners[i]]) is not { } at)
                    return null;

                flat[i] = at;
            }

            return flat;
        }

        private Vector3 Center(int face)
        {
            var sum = Vector3.Zero;

            foreach (var corner in edit.Mesh.Faces[face])
                sum += edit.Points[corner];

            return sum / edit.Mesh.Faces[face].Length;
        }

        private Vector3 LocalCenter(int face)
        {
            var sum = Vector3.Zero;

            foreach (var corner in edit.Mesh.Faces[face])
                sum += edit.Mesh.Points[corner];

            return sum / edit.Mesh.Faces[face].Length;
        }
    }
}
