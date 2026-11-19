using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public sealed class MeshRaycaster
    {
        private const int LeafSize = 4;
        private const float Parallel = 1e-12f;

        private readonly Vector3[] points;
        private readonly Triangle[] triangles;
        private readonly Node[] nodes;
        private readonly int[] order;
        private readonly float slack;

        public MeshRaycaster(MeshTopology mesh, IReadOnlySet<int>? skippedFaces = null)
        {
            points = mesh.Points;

            var found = new List<Triangle>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (skippedFaces is not null && skippedFaces.Contains(f))
                    continue;

                var corners = mesh.Faces[f];

                for (var i = 1; i + 1 < corners.Length; i++)
                    found.Add(new Triangle(corners[0], corners[i], corners[i + 1], f));
            }

            triangles = [.. found];
            order = [.. Enumerable.Range(0, triangles.Length)];

            var built = new List<Node>(Math.Max(1, triangles.Length * 2 / LeafSize));

            if (triangles.Length > 0)
                Build(built, 0, triangles.Length);

            nodes = [.. built];

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var point in points)
            {
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            slack = points.Length == 0 ? 1e-5f : MathF.Max(Vector3.Distance(min, max) * 1e-4f, 1e-6f);
        }

        public (int Face, float Distance)? Cast(in Vector3 origin, in Vector3 direction, float reach = float.MaxValue)
        {
            if (nodes.Length == 0)
                return null;

            var inverse = Invert(direction);
            var nearest = reach;
            var hit = -1;
            var stack = new Stack<int>();

            stack.Push(0);

            while (stack.Count > 0)
            {
                var node = nodes[stack.Pop()];

                if (!Crosses(node, origin, inverse, nearest))
                    continue;

                if (node.Count > 0)
                {
                    for (var i = node.Start; i < node.Start + node.Count; i++)
                    {
                        var triangle = triangles[order[i]];

                        if (Intersect(triangle, origin, direction) is { } distance
                            && distance > 0f
                            && distance < nearest)
                        {
                            nearest = distance;
                            hit = triangle.Face;
                        }
                    }

                    continue;
                }

                stack.Push(node.Left);
                stack.Push(node.Right);
            }

            return hit < 0 ? null : (hit, nearest);
        }

        public bool IsBlocked(in Vector3 from, in Vector3 to)
        {
            var span = to - from;
            var length = span.Length();

            if (length <= slack * 2f)
                return false;

            var direction = span / length;
            var start = from + direction * slack;

            return Cast(start, direction, length - slack * 2f) is not null;
        }

        private void Build(List<Node> built, int start, int count)
        {
            var index = built.Count;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var centerMin = new Vector3(float.MaxValue);
            var centerMax = new Vector3(float.MinValue);

            for (var i = start; i < start + count; i++)
            {
                var triangle = triangles[order[i]];
                var a = points[triangle.A];
                var b = points[triangle.B];
                var c = points[triangle.C];

                min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
                max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));

                var center = (a + b + c) / 3f;

                centerMin = Vector3.Min(centerMin, center);
                centerMax = Vector3.Max(centerMax, center);
            }

            built.Add(new Node(min, max, start, count, -1, -1));

            if (count <= LeafSize)
                return;

            var extent = centerMax - centerMin;
            var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;

            Array.Sort(order, start, count, Comparer<int>.Create((left, right) => Along(left, axis).CompareTo(Along(right, axis))));

            var half = count / 2;

            built[index] = new Node(min, max, start, 0, built.Count, -1);
            Build(built, start, half);

            var right = built.Count;

            built[index] = built[index] with { Right = right };
            Build(built, start + half, count - half);
        }

        private float Along(int triangle, int axis)
        {
            var found = triangles[triangle];
            var center = points[found.A] + points[found.B] + points[found.C];

            return axis switch
            {
                0 => center.X,
                1 => center.Y,
                _ => center.Z,
            };
        }

        private static Vector3 Invert(in Vector3 direction)
            => new(
                MathF.Abs(direction.X) > Parallel ? 1f / direction.X : float.MaxValue,
                MathF.Abs(direction.Y) > Parallel ? 1f / direction.Y : float.MaxValue,
                MathF.Abs(direction.Z) > Parallel ? 1f / direction.Z : float.MaxValue);

        private static bool Crosses(in Node node, in Vector3 origin, in Vector3 inverse, float reach)
        {
            var low = (node.Min - origin) * inverse;
            var high = (node.Max - origin) * inverse;
            var near = Vector3.Min(low, high);
            var far = Vector3.Max(low, high);
            var enter = MathF.Max(MathF.Max(near.X, near.Y), near.Z);
            var leave = MathF.Min(MathF.Min(far.X, far.Y), far.Z);

            return leave >= MathF.Max(enter, 0f) && enter <= reach;
        }

        private float? Intersect(in Triangle triangle, in Vector3 origin, in Vector3 direction)
        {
            var a = points[triangle.A];
            var first = points[triangle.B] - a;
            var second = points[triangle.C] - a;
            var across = Vector3.Cross(direction, second);
            var determinant = Vector3.Dot(first, across);

            if (MathF.Abs(determinant) < Parallel)
                return null;

            var inverse = 1f / determinant;
            var offset = origin - a;
            var u = Vector3.Dot(offset, across) * inverse;

            if (u is < 0f or > 1f)
                return null;

            var turned = Vector3.Cross(offset, first);
            var v = Vector3.Dot(direction, turned) * inverse;

            if (v < 0f || u + v > 1f)
                return null;

            return Vector3.Dot(second, turned) * inverse;
        }

        private readonly record struct Triangle(int A, int B, int C, int Face);

        private readonly record struct Node(Vector3 Min, Vector3 Max, int Start, int Count, int Left, int Right);
    }
}
