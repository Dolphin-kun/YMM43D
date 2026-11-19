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

        private static readonly Color4 SharpColor = new(0.2f, 0.8f, 1f, 1f);

        private static readonly Color4 CreaseColor = new(1f, 0.3f, 0.85f, 1f);

        private static readonly Color4 SeamColor = new(0.95f, 0.12f, 0.12f, 1f);

        private static readonly Color4 ChosenColor = new(1f, 0.45f, 0.08f, 1f);

        private static readonly Color4 ActiveColor = new(1f, 1f, 1f, 1f);

        private static readonly Color4 FillColor = new(1f, 0.45f, 0.08f, 0.22f);

        private static readonly Color4 CutColor = new(1f, 0.88f, 0.1f, 1f);

        public const float MarkerRatio = 0.009f;
        public const float HandleReach = 22f;
        public const float HandleRatio = 2.5f;
        private const int HandleSides = 16;
        public const float GrabThreshold = MeshPicker.Reach;

        private readonly DeviceResourceCache<EditResources> resources;

        public MeshEditRenderer()
        {
            resources = new DeviceResourceCache<EditResources>(device => new EditResources(device));
        }

        public void Draw(in Render3DContext render, in Matrix4x4 toWorld, MeshTopology mesh, MeshSelection selection, in MeshOverlay overlay)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            shared.Rebuild(mesh, selection, overlay);

            var transform = render.GetWorldViewProjection(toWorld);
            var constants = TransformConstants.CreateUnlit(transform, 1f);
            var fill = new DrawSettings
            {
                Blend = BlendMode.Normal,
                IgnoreDepth = overlay.XRay,
                SkipDepthWrite = true,
                LiftsLines = true,
            };
            var lines = new DrawSettings { IgnoreDepth = overlay.XRay, LiftsLines = true };

            foreach (var face in shared.Fills)
                shared.Pipeline.Draw(render.Context, constants, fill, face);

            foreach (var line in shared.Lines)
                shared.Pipeline.Draw(render.Context, constants, lines, line);
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

        public static Vector3? HandleAt(MeshTopology mesh, IReadOnlySet<int> selection, in Vector3 right)
            => selection.Count == 0 ? null : mesh.Middle(selection) + right * (MarkerSize(mesh) * HandleReach);

        public static void AddRing(List<Vector3> into, in Vector3 at, in Vector3 right, in Vector3 up, float size)
        {
            for (var i = 0; i < HandleSides; i++)
            {
                var from = MathF.Tau * i / HandleSides;
                var to = MathF.Tau * (i + 1) / HandleSides;

                into.Add(at + (right * MathF.Cos(from) + up * MathF.Sin(from)) * size);
                into.Add(at + (right * MathF.Cos(to) + up * MathF.Sin(to)) * size);
            }
        }

        public static void AddMarker(List<Vector3> into, in Vector3 at, in Vector3 right, in Vector3 up, float size)
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
            private readonly List<FillMesh> fills = [];

            private MeshTopology? builtMesh;
            private long builtSelection = long.MinValue;
            private MeshOverlay builtOverlay;

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(device, Vertex.InputElements, new VertexColorMaterial(device));

            public IReadOnlyList<LineMesh> Lines => lines;

            public IReadOnlyList<FillMesh> Fills => fills;

            public void Rebuild(MeshTopology mesh, MeshSelection selection, in MeshOverlay overlay)
            {
                if (ReferenceEquals(builtMesh, mesh)
                    && builtSelection == selection.Revision
                    && builtOverlay.Matches(overlay))
                {
                    return;
                }

                builtMesh = mesh;
                builtSelection = selection.Revision;
                builtOverlay = overlay;

                foreach (var line in lines)
                    line.Dispose();

                foreach (var face in fills)
                    face.Dispose();

                lines.Clear();
                fills.Clear();

                var plain = new List<Vector3>();
                var sharp = new List<Vector3>();
                var creased = new List<Vector3>();
                var seams = new List<Vector3>();
                var picked = new List<Vector3>();
                var active = new List<Vector3>();
                var part = selection.Part;

                for (var e = 0; e < mesh.Edges.Length; e++)
                {
                    if (!selection.IsEdgeShown(e))
                        continue;

                    var edge = mesh.Edges[e];
                    var into = part is MeshPart.Edges && selection.Active == e
                        ? active
                        : selection.Edges.Contains(e) ? picked
                        : mesh.MarkOf(e) is { Seam: true } ? seams
                        : mesh.MarkOf(e) is { Sharp: true } ? sharp
                        : mesh.MarkOf(e) is { Crease: > 0f } ? creased
                        : plain;

                    into.Add(mesh.Points[edge.A]);
                    into.Add(mesh.Points[edge.B]);
                }

                var size = MarkerSize(mesh);
                var dots = new List<Vector3>();
                var chosen = new List<Vector3>();

                if (part is MeshPart.Vertices)
                {
                    for (var i = 0; i < mesh.Points.Length; i++)
                    {
                        if (!selection.IsVertexShown(i))
                            continue;

                        var into = selection.Active == i ? active : selection.Vertices.Contains(i) ? chosen : dots;

                        AddMarker(into, mesh.Points[i], overlay.Right, overlay.Up, size);
                    }
                }

                if (part is MeshPart.Faces)
                {
                    for (var f = 0; f < mesh.Faces.Length; f++)
                    {
                        if (!selection.IsFaceShown(f))
                            continue;

                        var center = mesh.Middle(mesh.Faces[f]);
                        var into = selection.Active == f ? active : selection.Faces.Contains(f) ? chosen : dots;

                        AddMarker(into, center, overlay.Right, overlay.Up, size * 0.6f);
                    }
                }

                var shaded = new List<Vector3>();

                foreach (var f in selection.Faces)
                {
                    var corners = mesh.Faces[f];

                    for (var i = 1; i + 1 < corners.Length; i++)
                    {
                        shaded.Add(mesh.Points[corners[0]]);
                        shaded.Add(mesh.Points[corners[i]]);
                        shaded.Add(mesh.Points[corners[i + 1]]);
                    }
                }

                if (shaded.Count > 0)
                    fills.Add(new FillMesh(device, [.. shaded], FillColor));

                Add(plain, LineColor);
                Add(sharp, SharpColor);
                Add(creased, CreaseColor);
                Add(seams, SeamColor);
                Add(dots, LineColor);
                Add(picked, ChosenColor);
                Add(chosen, ChosenColor);
                Add(active, ActiveColor);

                if (overlay.CutEdge >= 0 && overlay.CutEdge < mesh.Edges.Length)
                    Add([.. mesh.CutPreview(overlay.CutEdge)], CutColor);

                if (overlay.ShowsHandle && HandleAt(mesh, selection.Vertices, overlay.Right) is { } handle)
                {
                    var grip = new List<Vector3> { mesh.Middle(selection.Vertices), handle };

                    AddRing(grip, handle, overlay.Right, overlay.Up, size * HandleRatio);
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

                foreach (var face in fills)
                    face.Dispose();

                lines.Clear();
                fills.Clear();
                Pipeline.Dispose();
            }
        }
    }
}
