using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace FrameImpact3D
{
    internal sealed class ImpactMesh : IMesh
    {
        private static readonly Vector2[] QuadCorners =
        [
            new(-1f, -1f),
            new(1f, -1f),
            new(-1f, 1f),
            new(-1f, 1f),
            new(1f, -1f),
            new(1f, 1f),
        ];

        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }

        public ID3D11Buffer? IndexBuffer => null;

        public int DrawCount { get; }

        public int VertexStride => ImpactVertex.Stride;

        public InputElementDescription[] InputElements => ImpactVertex.InputElements;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public ImpactPattern Pattern { get; }

        public ImpactMesh(ID3D11Device device, in ImpactPattern pattern)
        {
            Pattern = pattern;

            var vertices = Build(pattern);

            VertexBuffer = disposer.CollectEx(D3D11Buffers.Create(device, vertices, BindFlags.VertexBuffer));
            DrawCount = vertices.Length;
        }

        private static ImpactVertex[] Build(in ImpactPattern pattern)
        {
            var count = Math.Max(1, pattern.Count);
            var random = new Random(pattern.Seed);
            var vertices = new ImpactVertex[count * QuadCorners.Length];
            var v = 0;

            for (var i = 0; i < count; i++)
            {
                var (direction, seed) = pattern.Kind switch
                {
                    ImpactKind.Ring => (Vector3.Zero, new Vector4(i * ImpactPattern.RingStagger, 0f, 0f, 0f)),
                    ImpactKind.Lines => (new Vector3((i + random.NextSingle() * 0.8f) / count, random.NextSingle(), random.NextSingle()), Draw(random)),
                    ImpactKind.Sparks => (new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle()), Draw(random)),
                    _ => (Vector3.Zero, Vector4.Zero),
                };

                foreach (var corner in QuadCorners)
                    vertices[v++] = new ImpactVertex(direction, seed, corner);
            }

            return vertices;
        }

        private static Vector4 Draw(Random random) => new(random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle());

        public void Dispose() => disposer.Dispose();
    }
}
