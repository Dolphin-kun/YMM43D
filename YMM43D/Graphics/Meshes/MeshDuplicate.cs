using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshDuplicate
    {
        public static (MeshTopology Mesh, int[] Added)? Duplicate(this MeshTopology mesh, IReadOnlyCollection<int> faces, IReadOnlyCollection<int> edges, IReadOnlyCollection<int> vertices)
        {
            var (chosenFaces, chosenEdges, lone) = mesh.Parted(faces, edges, vertices);

            if (chosenFaces.Length == 0 && chosenEdges.Length == 0 && lone.Length == 0)
                return null;

            var points = new List<Vector3>(mesh.Points);
            var copies = new Dictionary<int, int>();

            int Copy(int vertex)
            {
                if (!copies.TryGetValue(vertex, out var copy))
                {
                    copy = points.Count;
                    copies[vertex] = copy;
                    points.Add(mesh.Points[vertex]);
                }

                return copy;
            }

            var made = new List<int[]>(mesh.Faces);
            var groups = new List<int>(mesh.Groups);
            var smooth = new List<bool>(mesh.Smooth);
            var uvs = mesh.Uvs is { } source ? new List<Vector2[]>(source) : null;

            foreach (var face in chosenFaces)
            {
                made.Add([.. mesh.Faces[face].Select(Copy)]);
                groups.Add(mesh.Groups[face]);
                smooth.Add(mesh.Smooth[face]);
                uvs?.Add(mesh.Uvs![face]);
            }

            MeshEdge[] loose = [.. mesh.LooseEdges, .. chosenEdges.Select(edge => new MeshEdge(Copy(mesh.Edges[edge].A), Copy(mesh.Edges[edge].B)))];

            foreach (var vertex in lone)
                Copy(vertex);

            var marks = new Dictionary<MeshEdge, EdgeMark>(mesh.Marks);

            foreach (var (edge, mark) in mesh.Marks)
            {
                if (copies.TryGetValue(edge.A, out var a) && copies.TryGetValue(edge.B, out var b))
                    marks[MeshTopology.Key(a, b)] = mark;
            }

            return (new MeshTopology([.. points], [.. made], [.. groups], [.. smooth], mesh.GroupCount, loose, marks, uvs is null ? null : [.. uvs]), [.. copies.Values]);
        }

        public static (MeshTopology Part, MeshTopology Left)? Separate(this MeshTopology mesh, IReadOnlyCollection<int> faces, IReadOnlyCollection<int> edges, IReadOnlyCollection<int> vertices)
        {
            var (chosenFaces, chosenEdges, lone) = mesh.Parted(faces, edges, vertices);
            var onFace = new HashSet<int>(chosenFaces);
            var onEdge = new HashSet<int>(chosenEdges);
            var alone = new HashSet<int>(lone);

            bool Picked(MeshEdge edge) => onEdge.Contains(mesh.FindEdge(edge.A, edge.B));

            if (MeshDelete.Keep(mesh, onFace.Contains, Picked, Picked, alone.Contains) is not { } part)
                return null;

            var rest = MeshDelete.Keep(mesh, face => !onFace.Contains(face), edge => !Picked(edge), _ => false, vertex => mesh.IsIsolated(vertex) && !alone.Contains(vertex));

            return (part.Carry(mesh), rest?.Carry(mesh) ?? mesh);
        }

        public static MeshTopology Join(this MeshTopology mesh, MeshTopology other, in Matrix4x4 toHere, bool keepsGroups = false)
        {
            var offset = mesh.Points.Length;
            var transform = toHere;
            Vector3[] points = [.. mesh.Points, .. other.Points.Select(point => Vector3.Transform(point, transform))];
            int[][] faces = [.. mesh.Faces, .. other.Faces.Select(face => face.Select(corner => corner + offset).ToArray())];
            int[] groups = [.. mesh.Groups, .. other.Groups.Select(group => keepsGroups ? group : Math.Min(group, mesh.GroupCount - 1))];
            bool[] smooth = [.. mesh.Smooth, .. other.Smooth];
            MeshEdge[] loose = [.. mesh.LooseEdges, .. other.LooseEdges.Select(edge => new MeshEdge(edge.A + offset, edge.B + offset))];

            if (transform.GetDeterminant() < 0f)
                faces = [.. faces.Select((face, index) => index < mesh.Faces.Length ? face : [.. face.Reverse()])];

            var marks = new Dictionary<MeshEdge, EdgeMark>(mesh.Marks);

            foreach (var (edge, mark) in other.Marks)
                marks[MeshTopology.Key(edge.A + offset, edge.B + offset)] = mark;

            var flipped = transform.GetDeterminant() < 0f;
            Vector2[][]? uvs = mesh.Uvs is null && other.Uvs is null ? null : [.. mesh.UvsOrGenerated(), .. other.UvsOrGenerated().Select(face => flipped ? [.. face.Reverse()] : face)];

            Vector3[][]? keys = mesh.Keys is { } known ? [.. known.Select(key => (Vector3[])[.. key, .. new Vector3[other.Points.Length]])] : null;

            return new MeshTopology(points, faces, groups, smooth, mesh.GroupCount, loose, marks, uvs, keys);
        }
    }
}
