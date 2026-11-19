using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal readonly record struct ProportionalEdit(float Radius, ProportionalFalloff Falloff, bool Connected)
    {
        public const float LeastRadius = 1e-4f;

        public Dictionary<int, float> Weights(MeshTopology mesh, Vector3[] placed, MeshSelection selection)
        {
            var reach = MathF.Max(Radius, LeastRadius);
            var distances = Connected ? AlongEdges(mesh, placed, selection, reach) : ThroughSpace(placed, selection, reach);
            var weights = new Dictionary<int, float>(distances.Count);

            foreach (var (vertex, distance) in distances)
            {
                var weight = selection.Vertices.Contains(vertex) ? 1f : Weigh(Math.Clamp(distance / reach, 0f, 1f), vertex);

                if (weight > 0f)
                    weights[vertex] = weight;
            }

            return weights;
        }

        private float Weigh(float along, int vertex)
        {
            var rest = 1f - along;

            return Falloff switch
            {
                ProportionalFalloff.Sphere => MathF.Sqrt(MathF.Max(2f * rest - rest * rest, 0f)),
                ProportionalFalloff.Root => MathF.Sqrt(rest),
                ProportionalFalloff.InverseSquare => rest * (2f - rest),
                ProportionalFalloff.Sharp => rest * rest,
                ProportionalFalloff.Linear => rest,
                ProportionalFalloff.Constant => along < 1f ? 1f : 0f,
                ProportionalFalloff.Random => rest * Scatter(vertex),
                _ => rest * rest * (3f - 2f * rest),
            };
        }

        private static float Scatter(int vertex)
        {
            var hash = (uint)vertex * 0x9E3779B1u;

            hash ^= hash >> 15;
            hash *= 0x2C1B3C6Du;
            hash ^= hash >> 12;

            return (hash & 0xFFFFFF) / (float)0x1000000;
        }

        private static Dictionary<int, float> ThroughSpace(Vector3[] placed, MeshSelection selection, float reach)
        {
            var cells = new Dictionary<(int, int, int), List<int>>();

            (int, int, int) CellOf(in Vector3 point)
                => ((int)MathF.Floor(point.X / reach), (int)MathF.Floor(point.Y / reach), (int)MathF.Floor(point.Z / reach));

            foreach (var vertex in selection.Vertices)
            {
                var cell = CellOf(placed[vertex]);

                if (!cells.TryGetValue(cell, out var list))
                    cells[cell] = list = [];

                list.Add(vertex);
            }

            var found = new Dictionary<int, float>();

            for (var v = 0; v < placed.Length; v++)
            {
                if (!selection.IsVertexShown(v))
                    continue;

                if (selection.Vertices.Contains(v))
                {
                    found[v] = 0f;
                    continue;
                }

                var (x, y, z) = CellOf(placed[v]);
                var nearest = float.MaxValue;

                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            if (!cells.TryGetValue((x + dx, y + dy, z + dz), out var list))
                                continue;

                            foreach (var other in list)
                                nearest = MathF.Min(nearest, Vector3.Distance(placed[v], placed[other]));
                        }
                    }
                }

                if (nearest < reach)
                    found[v] = nearest;
            }

            return found;
        }

        private static Dictionary<int, float> AlongEdges(MeshTopology mesh, Vector3[] placed, MeshSelection selection, float reach)
        {
            var found = new Dictionary<int, float>();
            var queue = new PriorityQueue<int, float>();

            foreach (var vertex in selection.Vertices)
            {
                found[vertex] = 0f;
                queue.Enqueue(vertex, 0f);
            }

            while (queue.TryDequeue(out var vertex, out var distance))
            {
                if (distance > found[vertex])
                    continue;

                foreach (var edge in mesh.VertexEdges[vertex])
                {
                    var other = mesh.Edges[edge].Other(vertex);

                    if (!selection.IsVertexShown(other))
                        continue;

                    var through = distance + Vector3.Distance(placed[vertex], placed[other]);

                    if (through >= reach || (found.TryGetValue(other, out var known) && known <= through))
                        continue;

                    found[other] = through;
                    queue.Enqueue(other, through);
                }
            }

            return found;
        }
    }
}
