using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshBoolean
    {
        private const float Epsilon = 1e-5f;
        private const float Weld = 1e-4f;

        public static MeshTopology Combined(this MeshTopology mesh, MeshTopology other, BooleanOperation operation)
        {
            if (mesh.Faces.Length == 0)
                return operation is BooleanOperation.Union ? other : mesh;

            if (other.Faces.Length == 0)
                return operation is BooleanOperation.Intersect ? Polygons.ToMesh([], mesh) : mesh;

            var a = new Node(Polygons.From(mesh, false));
            var b = new Node(Polygons.From(other, true));

            switch (operation)
            {
                case BooleanOperation.Union:
                    a.ClipTo(b);
                    b.ClipTo(a);
                    b.Invert();
                    b.ClipTo(a);
                    b.Invert();
                    a.Build(b.All());
                    break;

                case BooleanOperation.Intersect:
                    a.Invert();
                    b.ClipTo(a);
                    b.Invert();
                    a.ClipTo(b);
                    b.ClipTo(a);
                    a.Build(b.All());
                    a.Invert();
                    break;

                default:
                    a.Invert();
                    a.ClipTo(b);
                    b.ClipTo(a);
                    b.Invert();
                    b.ClipTo(a);
                    b.Invert();
                    a.Build(b.All());
                    a.Invert();
                    break;
            }

            return Polygons.ToMesh(a.All(), mesh);
        }

        private readonly record struct Plane(Vector3 Normal, float W)
        {
            public static Plane? Through(Vector3[] points)
            {
                var origin = points[0];
                var normal = Vector3.Zero;
                var center = Vector3.Zero;

                for (var i = 0; i < points.Length; i++)
                {
                    normal += Vector3.Cross(points[(i + 1) % points.Length] - origin, points[i] - origin);
                    center += points[i] - origin;
                }

                if (normal.LengthSquared() < 1e-20f)
                    return null;

                normal = Vector3.Normalize(normal);

                return new Plane(normal, Vector3.Dot(normal, origin + center / points.Length));
            }

            public Plane Flipped => new(-Normal, -W);

            public float Distance(Vector3 point) => Vector3.Dot(Normal, point) - W;
        }

        private sealed record Polygon(Vector3[] Points, Vector2[] Uvs, Plane Plane, int Group, bool Smooth)
        {
            public Polygon Flipped => this with { Points = [.. Points.Reverse()], Uvs = [.. Uvs.Reverse()], Plane = Plane.Flipped };
        }

        private static class Polygons
        {
            public static List<Polygon> From(MeshTopology mesh, bool other)
            {
                var result = new List<Polygon>();
                var source = mesh.Uvs is null ? mesh.WithUvs(mesh.Generated()) : mesh;
                var triangles = source.Triangulate([.. Enumerable.Range(0, source.Faces.Length)])?.Mesh ?? source;
                var uvs = triangles.UvsOrGenerated();

                for (var f = 0; f < triangles.Faces.Length; f++)
                {
                    Vector3[] corners = [.. triangles.Faces[f].Select(corner => triangles.Points[corner])];

                    if (Plane.Through(corners) is { } plane)
                        result.Add(new Polygon(corners, uvs[f], plane, other ? -1 : triangles.Groups[f], triangles.Smooth[f]));
                }

                return result;
            }

            public static MeshTopology ToMesh(List<Polygon> polygons, MeshTopology source)
            {
                var places = new Dictionary<(long, long, long), int>();
                var points = new List<Vector3>();
                var faces = new List<int[]>();
                var groups = new List<int>();
                var smooth = new List<bool>();
                var uvs = new List<Vector2[]>();

                int Index(Vector3 point)
                {
                    var key = ((long)MathF.Round(point.X / Weld), (long)MathF.Round(point.Y / Weld), (long)MathF.Round(point.Z / Weld));

                    if (!places.TryGetValue(key, out var index))
                    {
                        index = points.Count;
                        places[key] = index;
                        points.Add(point);
                    }

                    return index;
                }

                var fallback = source.Groups.Length > 0 ? source.Groups[0] : 0;

                foreach (var polygon in polygons)
                {
                    var corners = new List<int>();
                    var faceUvs = new List<Vector2>();

                    for (var i = 0; i < polygon.Points.Length; i++)
                    {
                        var index = Index(polygon.Points[i]);

                        if (corners.Count > 0 && corners[^1] == index)
                            continue;

                        corners.Add(index);
                        faceUvs.Add(polygon.Uvs[i]);
                    }

                    while (corners.Count > 1 && corners[0] == corners[^1])
                    {
                        corners.RemoveAt(corners.Count - 1);
                        faceUvs.RemoveAt(faceUvs.Count - 1);
                    }

                    if (corners.Count < 3 || corners.Distinct().Count() != corners.Count)
                        continue;

                    faces.Add([.. corners]);
                    uvs.Add([.. faceUvs]);
                    groups.Add(polygon.Group < 0 ? fallback : polygon.Group);
                    smooth.Add(polygon.Smooth);
                }

                return new MeshTopology([.. points], [.. faces], [.. groups], [.. smooth], source.GroupCount, null, null, [.. uvs]);
            }
        }

        private sealed class Node
        {
            private Plane? plane;
            private Node? front;
            private Node? back;
            private List<Polygon> polygons = [];

            public Node(List<Polygon> polygons) => Build(polygons);

            private Node()
            {
            }

            public void Invert()
            {
                var stack = new Stack<Node>([this]);

                while (stack.Count > 0)
                {
                    var node = stack.Pop();

                    node.polygons = [.. node.polygons.Select(polygon => polygon.Flipped)];
                    node.plane = node.plane?.Flipped;
                    (node.front, node.back) = (node.back, node.front);

                    if (node.front is { } f)
                        stack.Push(f);

                    if (node.back is { } b)
                        stack.Push(b);
                }
            }

            public List<Polygon> Clip(List<Polygon> list)
            {
                if (plane is not { } cut)
                    return [.. list];

                var frontList = new List<Polygon>();
                var backList = new List<Polygon>();

                foreach (var polygon in list)
                    Split(cut, polygon, frontList, backList, frontList, backList);

                frontList = front is { } f ? f.Clip(frontList) : frontList;
                backList = back is { } b ? b.Clip(backList) : [];

                frontList.AddRange(backList);

                return frontList;
            }

            public void ClipTo(Node other)
            {
                var stack = new Stack<Node>([this]);

                while (stack.Count > 0)
                {
                    var node = stack.Pop();

                    node.polygons = other.Clip(node.polygons);

                    if (node.front is { } f)
                        stack.Push(f);

                    if (node.back is { } b)
                        stack.Push(b);
                }
            }

            public List<Polygon> All()
            {
                var result = new List<Polygon>();
                var stack = new Stack<Node>([this]);

                while (stack.Count > 0)
                {
                    var node = stack.Pop();

                    result.AddRange(node.polygons);

                    if (node.front is { } f)
                        stack.Push(f);

                    if (node.back is { } b)
                        stack.Push(b);
                }

                return result;
            }

            public void Build(List<Polygon> list)
            {
                var work = new Stack<(Node Node, List<Polygon> List)>([(this, list)]);

                while (work.Count > 0)
                {
                    var (node, items) = work.Pop();

                    if (items.Count == 0)
                        continue;

                    var start = 0;

                    if (node.plane is null)
                    {
                        node.plane = items[0].Plane;
                        node.polygons.Add(items[0]);
                        start = 1;
                    }

                    var frontList = new List<Polygon>();
                    var backList = new List<Polygon>();

                    for (var i = start; i < items.Count; i++)
                        Split(node.plane.Value, items[i], node.polygons, node.polygons, frontList, backList);

                    if (frontList.Count > 0)
                        work.Push((node.front ??= new Node(), frontList));

                    if (backList.Count > 0)
                        work.Push((node.back ??= new Node(), backList));
                }
            }

            private static void Split(Plane cut, Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack, List<Polygon> frontList, List<Polygon> backList)
            {
                const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

                var kinds = new int[polygon.Points.Length];
                var whole = 0;

                for (var i = 0; i < kinds.Length; i++)
                {
                    var distance = cut.Distance(polygon.Points[i]);

                    kinds[i] = distance < -Epsilon ? Back : distance > Epsilon ? Front : Coplanar;
                    whole |= kinds[i];
                }

                switch (whole)
                {
                    case Coplanar:
                        (Vector3.Dot(cut.Normal, polygon.Plane.Normal) > 0f ? coplanarFront : coplanarBack).Add(polygon);
                        return;

                    case Front:
                        frontList.Add(polygon);
                        return;

                    case Back:
                        backList.Add(polygon);
                        return;
                }

                var frontPoints = new List<Vector3>();
                var backPoints = new List<Vector3>();
                var frontUvs = new List<Vector2>();
                var backUvs = new List<Vector2>();

                for (var i = 0; i < kinds.Length; i++)
                {
                    var j = (i + 1) % kinds.Length;
                    var (here, next) = (polygon.Points[i], polygon.Points[j]);

                    if (kinds[i] != Back)
                    {
                        frontPoints.Add(here);
                        frontUvs.Add(polygon.Uvs[i]);
                    }

                    if (kinds[i] != Front)
                    {
                        backPoints.Add(here);
                        backUvs.Add(polygon.Uvs[i]);
                    }

                    if ((kinds[i] | kinds[j]) == Spanning)
                    {
                        var t = cut.Distance(here) / (cut.Distance(here) - cut.Distance(next));
                        var crossing = Vector3.Lerp(here, next, t);
                        var crossingUv = Vector2.Lerp(polygon.Uvs[i], polygon.Uvs[j], t);

                        frontPoints.Add(crossing);
                        backPoints.Add(crossing);
                        frontUvs.Add(crossingUv);
                        backUvs.Add(crossingUv);
                    }
                }

                if (frontPoints.Count >= 3)
                    frontList.Add(polygon with { Points = [.. frontPoints], Uvs = [.. frontUvs] });

                if (backPoints.Count >= 3)
                    backList.Add(polygon with { Points = [.. backPoints], Uvs = [.. backUvs] });
            }
        }
    }
}
