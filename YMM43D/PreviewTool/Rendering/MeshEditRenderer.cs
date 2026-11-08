using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class MeshEditRenderer : IDisposable
    {
        private static readonly Color4 LineColor = new(0f, 0f, 0f, 1f);

        private static readonly Color4 ChosenColor = new(1f, 0.45f, 0.08f, 1f);

        private static readonly Color4 CutColor = new(1f, 0.88f, 0.1f, 1f);

        public const float MarkerRatio = 0.009f;

        public const float HandleReach = 22f;

        public const float HandleRatio = 2.5f;

        private const int HandleSides = 16;

        public const float GrabThreshold = 10f;

        private readonly DeviceResourceCache<EditResources> resources;

        public MeshEditRenderer()
        {
            resources = new DeviceResourceCache<EditResources>(device => new EditResources(device));
        }

        public void Draw(
            in Render3DContext render,
            in Matrix4x4 toWorld,
            MeshTopology mesh,
            IReadOnlySet<int> selection,
            MeshPart part,
            long revision,
            in Vector3 right,
            in Vector3 up,
            int cutEdge,
            bool showsHandle)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            shared.Rebuild(mesh, selection, part, revision, right, up, cutEdge, showsHandle);

            var transform = render.GetWorldViewProjection(toWorld);
            var settings = new DrawSettings { LiftsLines = true };
            var constants = TransformConstants.CreateUnlit(transform, 1f);

            foreach (var line in shared.Lines)
                shared.Pipeline.Draw(render.Context, constants, settings, line);
        }

        public void Dispose() => resources.Dispose();

        public static float MarkerSize(MeshTopology mesh)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var point in mesh.Points)
            {
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            return MathF.Max(Vector3.Distance(min, max) * MarkerRatio, 1e-5f);
        }

        public static Vector3? HandleAt(
            MeshTopology mesh, IReadOnlySet<int> selection, in Vector3 right)
            => selection.Count == 0
                ? null
                : mesh.Middle(selection) + right * (MarkerSize(mesh) * HandleReach);

        public static void AddRing(
            List<Vector3> into, in Vector3 at, in Vector3 right, in Vector3 up, float size)
        {
            for (var i = 0; i < HandleSides; i++)
            {
                var from = MathF.Tau * i / HandleSides;
                var to = MathF.Tau * (i + 1) / HandleSides;

                into.Add(at + (right * MathF.Cos(from) + up * MathF.Sin(from)) * size);
                into.Add(at + (right * MathF.Cos(to) + up * MathF.Sin(to)) * size);
            }
        }

        public static void AddMarker(
            List<Vector3> into, in Vector3 at, in Vector3 right, in Vector3 up, float size)
        {
            var across = right * size;
            var along = up * size;

            Span<Vector3> corners =
            [
                at - across - along,
                at + across - along,
                at + across + along,
                at - across + along,
            ];

            for (var i = 0; i < corners.Length; i++)
            {
                into.Add(corners[i]);
                into.Add(corners[(i + 1) % corners.Length]);
            }
        }

        private sealed class EditResources(ID3D11Device device) : IDisposable
        {
            private readonly List<LineMesh> lines = [];

            private long builtRevision = long.MinValue;
            private MeshTopology? builtMesh;
            private Vector3 builtRight;
            private Vector3 builtUp;
            private int builtCut = -2;
            private bool builtHandle;

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            public IReadOnlyList<LineMesh> Lines => lines;

            public void Rebuild(
                MeshTopology mesh,
                IReadOnlySet<int> selection,
                MeshPart part,
                long revision,
                in Vector3 right,
                in Vector3 up,
                int cutEdge,
                bool showsHandle)
            {
                if (revision == builtRevision
                    && ReferenceEquals(builtMesh, mesh)
                    && cutEdge == builtCut
                    && showsHandle == builtHandle
                    && Vector3.Distance(builtRight, right) < 0.01f
                    && Vector3.Distance(builtUp, up) < 0.01f)
                {
                    return;
                }

                builtRevision = revision;
                builtMesh = mesh;
                builtRight = right;
                builtUp = up;
                builtCut = cutEdge;
                builtHandle = showsHandle;

                foreach (var line in lines)
                    line.Dispose();

                lines.Clear();

                var plain = new List<Vector3>();
                var picked = new List<Vector3>();

                foreach (var edge in mesh.Edges)
                {
                    var into = selection.Contains(edge.A) && selection.Contains(edge.B) ? picked : plain;

                    into.Add(mesh.Points[edge.A]);
                    into.Add(mesh.Points[edge.B]);
                }

                var size = MarkerSize(mesh);
                var dots = new List<Vector3>();
                var chosen = new List<Vector3>();

                if (part is MeshPart.Vertices)
                {
                    for (var i = 0; i < mesh.Points.Length; i++)
                        AddMarker(selection.Contains(i) ? chosen : dots, mesh.Points[i], right, up, size);
                }

                Add(plain, LineColor);
                Add(dots, LineColor);
                Add(picked, ChosenColor);
                Add(chosen, ChosenColor);

                if (cutEdge >= 0)
                    Add([.. mesh.CutPreview(cutEdge)], CutColor);

                if (showsHandle && HandleAt(mesh, selection, right) is { } handle)
                {
                    var grip = new List<Vector3> { mesh.Middle(selection), handle };

                    AddRing(grip, handle, right, up, size * HandleRatio);
                    Add(grip, CutColor);
                }
            }

            private void Add(List<Vector3> points, in Color4 color)
            {
                if (points.Count > 0)
                    lines.Add(new LineMesh(device, [.. points], color));
            }

            public void Dispose()
            {
                foreach (var line in lines)
                    line.Dispose();

                lines.Clear();
                Pipeline.Dispose();
            }
        }
    }
}
