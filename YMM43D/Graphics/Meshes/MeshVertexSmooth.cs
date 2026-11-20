using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshVertexSmooth
    {
        public static (MeshTopology Mesh, int[] Added)? Smoothed(this MeshTopology mesh, IReadOnlyCollection<int> vertices, float factor, int repeat)
        {
            var chosen = mesh.ExistingPoints(vertices);

            if (chosen.Length == 0 || factor <= 0f || repeat < 1)
                return null;

            var neighbors = chosen.Select(vertex => Neighbors(mesh, vertex)).ToArray();
            var points = (Vector3[])mesh.Points.Clone();
            var next = new Vector3[chosen.Length];

            for (var pass = 0; pass < repeat; pass++)
            {
                for (var i = 0; i < chosen.Length; i++)
                {
                    var around = neighbors[i];
                    var here = points[chosen[i]];

                    if (around.Length == 0)
                    {
                        next[i] = here;
                        continue;
                    }

                    var sum = Vector3.Zero;

                    foreach (var other in around)
                        sum += points[other];

                    next[i] = Vector3.Lerp(here, sum / around.Length, factor);
                }

                for (var i = 0; i < chosen.Length; i++)
                    points[chosen[i]] = next[i];
            }

            return (mesh.WithPoints(points), chosen);
        }

        private static int[] Neighbors(MeshTopology mesh, int vertex)
        {
            var edges = mesh.VertexEdges[vertex];
            var rim = edges.Where(edge => mesh.EdgeFaces[edge].Length == 1).ToArray();
            var along = rim.Length == 2 ? rim : edges;

            return [.. along.Select(edge => mesh.Edges[edge].Other(vertex)).Distinct()];
        }
    }
}
