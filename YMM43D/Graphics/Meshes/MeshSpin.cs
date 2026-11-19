using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshSpin
    {
        public static (MeshTopology Mesh, int[] Added)? Spin(this MeshTopology mesh, IReadOnlyCollection<int> faces, IReadOnlyCollection<int> edges, IReadOnlyCollection<int> vertices, Matrix4x4 turn, int steps, bool closes)
        {
            var count = Math.Max(1, steps);
            var current = mesh;
            var (nowFaces, nowEdges, nowVertices) = (faces, edges, vertices);
            var first = new Dictionary<int, int>();

            for (var step = 0; step < count; step++)
            {
                if (current.Extruded(nowFaces, nowEdges, nowVertices) is not { } made)
                    return null;

                var points = (Vector3[])made.Mesh.Points.Clone();

                foreach (var copy in made.Copies.Values)
                    points[copy] = Vector3.Transform(points[copy], turn);

                current = made.Mesh.WithPoints(points);

                if (step == 0)
                {
                    foreach (var (from, copy) in made.Copies)
                        first[copy] = from;
                }
                else
                {
                    first = made.Copies.Where(pair => first.ContainsKey(pair.Key)).ToDictionary(pair => pair.Value, pair => first[pair.Key]);
                }

                var copies = made.Copies.Values.ToHashSet();

                nowVertices = copies;
                nowEdges = [.. Enumerable.Range(0, current.Edges.Length).Where(edge => copies.Contains(current.Edges[edge].A) && copies.Contains(current.Edges[edge].B))];
                nowFaces = current.FacesIn(copies);
            }

            if (closes)
                return current.MergeInto(first, first.Values);

            return (current, [.. nowVertices]);
        }
    }
}
