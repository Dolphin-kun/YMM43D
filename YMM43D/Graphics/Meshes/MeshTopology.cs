using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public readonly record struct MeshEdge(int A, int B)
    {
        public int Other(int vertex) => vertex == A ? B : A;

        public bool Holds(int vertex) => vertex == A || vertex == B;
    }

    public sealed class MeshTopology
    {
        private const float Weld = 1e-4f;

        public Vector3[] Points { get; }

        public int[][] Faces { get; }

        public int[] Groups { get; }

        public bool[] Smooth { get; }

        public int GroupCount { get; }

        public MeshEdge[] Edges { get; }

        public int[][] EdgeFaces { get; }

        public int[][] VertexEdges { get; }

        public int[][] FaceEdges { get; }

        public MeshTopology(Vector3[] points, int[][] faces, int[] groups, bool[] smooth, int groupCount)
        {
            Points = points;
            Faces = faces;
            Groups = groups;
            Smooth = smooth;
            GroupCount = Math.Max(1, groupCount);

            var numbers = new Dictionary<(int, int), int>();
            var edges = new List<MeshEdge>();
            var faceEdges = new int[faces.Length][];
            var edgeFaces = new List<List<int>>();
            var vertexEdges = new List<int>[points.Length];

            for (var i = 0; i < points.Length; i++)
                vertexEdges[i] = [];

            for (var f = 0; f < faces.Length; f++)
            {
                var corners = faces[f];
                var found = new int[corners.Length];

                for (var i = 0; i < corners.Length; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % corners.Length];
                    var key = a < b ? (a, b) : (b, a);

                    if (!numbers.TryGetValue(key, out var index))
                    {
                        index = edges.Count;
                        numbers[key] = index;
                        edges.Add(new MeshEdge(key.Item1, key.Item2));
                        edgeFaces.Add([]);

                        vertexEdges[key.Item1].Add(index);
                        vertexEdges[key.Item2].Add(index);
                    }

                    found[i] = index;

                    if (!edgeFaces[index].Contains(f))
                        edgeFaces[index].Add(f);
                }

                faceEdges[f] = found;
            }

            Edges = [.. edges];
            FaceEdges = faceEdges;
            EdgeFaces = [.. edgeFaces.Select(one => one.ToArray())];
            VertexEdges = [.. vertexEdges.Select(one => one.ToArray())];
        }

        public static MeshTopology FromGeometry(SurfaceGeometry geometry)
        {
            var points = new List<Vector3>();
            var numbers = new Dictionary<(long, long, long), int>();
            var moved = new int[geometry.Vertices.Length];

            for (var i = 0; i < geometry.Vertices.Length; i++)
            {
                var point = geometry.Vertices[i];
                var key = (Quantize(point.X), Quantize(point.Y), Quantize(point.Z));

                if (!numbers.TryGetValue(key, out var index))
                {
                    index = points.Count;
                    numbers[key] = index;
                    points.Add(point);
                }

                moved[i] = index;
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            foreach (var face in geometry.Faces)
            {
                var corners = new List<int>();

                foreach (var corner in face.Indices)
                {
                    var index = moved[corner];

                    if (corners.Count == 0 || corners[^1] != index)
                        corners.Add(index);
                }

                while (corners.Count > 1 && corners[0] == corners[^1])
                    corners.RemoveAt(corners.Count - 1);

                if (corners.Count < 3)
                    continue;

                faces.Add([.. corners]);
                groups.Add(face.Group);
                smooth.Add(face.IsSmooth);
            }

            return new MeshTopology(
                [.. points], [.. faces], [.. groups], [.. smooth], geometry.GroupCount);
        }

        private static long Quantize(float value) => (long)MathF.Round(value / Weld);

        public SurfaceGeometry ToGeometry()
        {
            var faceNormals = new Vector3[Faces.Length];

            for (var f = 0; f < Faces.Length; f++)
                faceNormals[f] = NormalOf(f);

            var normals = new Vector3[Points.Length];
            var smoothed = new Vector3[Points.Length];

            for (var f = 0; f < Faces.Length; f++)
            {
                foreach (var corner in Faces[f])
                {
                    normals[corner] += faceNormals[f];

                    if (Smooth[f])
                        smoothed[corner] += faceNormals[f];
                }
            }

            for (var i = 0; i < normals.Length; i++)
            {
                var chosen = smoothed[i].LengthSquared() > 1e-12f ? smoothed[i] : normals[i];

                normals[i] = chosen.LengthSquared() > 1e-12f ? Vector3.Normalize(chosen) : Vector3.UnitY;
            }

            var faces = new SurfaceFace[Faces.Length];

            for (var f = 0; f < Faces.Length; f++)
                faces[f] = new SurfaceFace([.. Faces[f]], Groups[f], Smooth[f]);

            return new SurfaceGeometry([.. Points], normals, faces, GroupCount);
        }

        private Vector3 NormalOf(int face)
        {
            var corners = Faces[face];
            var sum = Vector3.Zero;

            for (var i = 0; i < corners.Length; i++)
            {
                var here = Points[corners[i]];
                var next = Points[corners[(i + 1) % corners.Length]];

                sum += new Vector3(
                    (here.Y - next.Y) * (here.Z + next.Z),
                    (here.Z - next.Z) * (here.X + next.X),
                    (here.X - next.X) * (here.Y + next.Y));
            }

            return sum.LengthSquared() > 1e-16f ? -Vector3.Normalize(sum) : Vector3.Zero;
        }

        public int FindEdge(int a, int b)
        {
            foreach (var index in VertexEdges[a])
            {
                if (Edges[index].Holds(b))
                    return index;
            }

            return -1;
        }

        public bool SharesFace(int first, int second)
        {
            foreach (var face in EdgeFaces[first])
            {
                if (EdgeFaces[second].Contains(face))
                    return true;
            }

            return false;
        }

        public int[] EdgeLoop(int edge)
        {
            if (edge < 0 || edge >= Edges.Length)
                return [];

            var found = WalkAcrossVertices(edge);

            if (found.Length > 1)
                return found;

            var border = Perimeter(edge);

            return border.Length > 1 ? border : EdgeRing(edge);
        }

        private int[] Perimeter(int edge)
        {
            foreach (var face in EdgeFaces[edge])
            {
                if (FaceEdges[face].Length > 4)
                    return [.. FaceEdges[face]];
            }

            return [];
        }

        private int[] WalkAcrossVertices(int start)
        {
            var loop = new List<int> { start };
            var taken = new HashSet<int> { start };

            foreach (var end in new[] { Edges[start].A, Edges[start].B })
            {
                var current = start;
                var at = end;

                while (true)
                {
                    var next = AcrossVertex(current, at);

                    if (next < 0 || !taken.Add(next))
                        break;

                    loop.Add(next);
                    at = Edges[next].Other(at);
                    current = next;
                }
            }

            return [.. loop];
        }

        private int AcrossVertex(int edge, int vertex)
        {
            var around = VertexEdges[vertex];

            if (around.Length != 4)
                return -1;

            foreach (var candidate in around)
            {
                if (candidate != edge && !SharesFace(candidate, edge))
                    return candidate;
            }

            return -1;
        }

        public int[] EdgeRing(int edge)
        {
            if (edge < 0 || edge >= Edges.Length)
                return [];

            var ring = new List<int> { edge };
            var taken = new HashSet<int> { edge };

            foreach (var side in EdgeFaces[edge])
            {
                var current = edge;
                var through = side;

                while (true)
                {
                    var next = AcrossFace(through, current);

                    if (next < 0 || !taken.Add(next))
                        break;

                    ring.Add(next);

                    var following = -1;

                    foreach (var other in EdgeFaces[next])
                    {
                        if (other != through)
                            following = other;
                    }

                    if (following < 0)
                        break;

                    current = next;
                    through = following;
                }
            }

            return [.. ring];
        }

        private int AcrossFace(int face, int edge)
        {
            var edges = FaceEdges[face];

            if (edges.Length != 4)
                return -1;

            for (var i = 0; i < edges.Length; i++)
            {
                if (edges[i] == edge)
                    return edges[(i + 2) % 4];
            }

            return -1;
        }

        public (MeshTopology Mesh, int[] Added)? CutLoop(int edge)
        {
            var ring = EdgeRing(edge);

            if (ring.Length == 0)
                return null;

            var onRing = new HashSet<int>(ring);
            var points = new List<Vector3>(Points);
            var middle = new Dictionary<int, int>();
            var added = new List<int>();

            foreach (var one in ring)
            {
                points.Add((Points[Edges[one].A] + Points[Edges[one].B]) / 2f);
                middle[one] = points.Count - 1;
                added.Add(points.Count - 1);
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();
            var split = 0;

            for (var f = 0; f < Faces.Length; f++)
            {
                var edges = FaceEdges[f];
                var at = CutCorner(f, onRing);

                if (at < 0)
                {
                    faces.Add(Faces[f]);
                    groups.Add(Groups[f]);
                    smooth.Add(Smooth[f]);

                    continue;
                }

                var corners = Faces[f];

                int Corner(int step) => corners[(at + step) % 4];

                var first = middle[edges[at]];
                var second = middle[edges[(at + 2) % 4]];

                faces.Add([Corner(0), first, second, Corner(3)]);
                groups.Add(Groups[f]);
                smooth.Add(Smooth[f]);

                faces.Add([first, Corner(1), Corner(2), second]);
                groups.Add(Groups[f]);
                smooth.Add(Smooth[f]);

                split++;
            }

            if (split == 0)
                return null;

            return (
                new MeshTopology([.. points], [.. faces], [.. groups], [.. smooth], GroupCount),
                [.. added]);
        }

        private int CutCorner(int face, HashSet<int> onRing)
        {
            var edges = FaceEdges[face];
            var hit = new List<int>();

            for (var i = 0; i < edges.Length; i++)
            {
                if (onRing.Contains(edges[i]))
                    hit.Add(i);
            }

            return edges.Length == 4 && hit.Count == 2 && hit[1] - hit[0] == 2 ? hit[0] : -1;
        }

        public Vector3[] PointNormals()
        {
            var normals = new Vector3[Points.Length];

            for (var f = 0; f < Faces.Length; f++)
            {
                var normal = NormalOf(f);

                foreach (var corner in Faces[f])
                    normals[corner] += normal;
            }

            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].LengthSquared() > 1e-12f
                    ? Vector3.Normalize(normals[i])
                    : Vector3.Zero;
            }

            return normals;
        }

        public Vector3[] CutPreview(int edge)
        {
            if (edge < 0 || edge >= Edges.Length)
                return [];

            var ring = EdgeRing(edge);

            if (ring.Length == 0)
                return [];

            var onRing = new HashSet<int>(ring);
            var lines = new List<Vector3>();

            Vector3 Middle(int one) => (Points[Edges[one].A] + Points[Edges[one].B]) / 2f;

            for (var f = 0; f < Faces.Length; f++)
            {
                var at = CutCorner(f, onRing);

                if (at < 0)
                    continue;

                var edges = FaceEdges[f];

                lines.Add(Middle(edges[at]));
                lines.Add(Middle(edges[(at + 2) % 4]));
            }

            return [.. lines];
        }

        private MeshTopology(MeshTopology from, Vector3[] points)
        {
            Points = points;
            Faces = from.Faces;
            Groups = from.Groups;
            Smooth = from.Smooth;
            GroupCount = from.GroupCount;
            Edges = from.Edges;
            EdgeFaces = from.EdgeFaces;
            VertexEdges = from.VertexEdges;
            FaceEdges = from.FaceEdges;
        }

        public MeshTopology WithPoints(Vector3[] points)
            => points.Length != Points.Length ? this : new MeshTopology(this, points);

        public Vector3 Middle(IReadOnlyCollection<int> vertices)
        {
            if (vertices.Count == 0)
                return Vector3.Zero;

            var sum = Vector3.Zero;

            foreach (var vertex in vertices)
            {
                if (vertex >= 0 && vertex < Points.Length)
                    sum += Points[vertex];
            }

            return sum / vertices.Count;
        }
    }
}
