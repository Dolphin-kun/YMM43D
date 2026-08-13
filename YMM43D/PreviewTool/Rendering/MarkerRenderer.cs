using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class MarkerRenderer : IDisposable
    {
        private static readonly Color4 LightColor = new(1f, 0.84f, 0.31f, 1f);

        private static readonly Color4 ReachColor = new(0.65f, 0.55f, 0.22f, 1f);

        private static readonly Color4 EnvironmentColor = new(0.42f, 0.66f, 0.85f, 1f);

        private const int RingSegments = 24;

        private const int RayCount = 8;

        private readonly DeviceResourceCache<MarkerResources> resources;

        public MarkerRenderer()
        {
            resources = new DeviceResourceCache<MarkerResources>(device => new MarkerResources(device));
        }

        public void Draw(
            in Render3DContext render,
            IReadOnlyList<SceneMarkerResolver.PlacedMarker> markers,
            IItem? selected)
        {
            if (markers.Count == 0)
                return;

            var shared = resources.Get(render.Device);

            foreach (var placed in markers)
                shared.Draw(render, placed.Marker, ReferenceEquals(placed.Item, selected));
        }

        public void Dispose() => resources.Dispose();

        private static Vector3[] BuildOutline(in SceneMarker marker) => marker.Kind switch
        {
            MarkerKind.DirectionalLight => Sun(marker.Direction),
            MarkerKind.PointLight => Globe(SceneMarker.BodyRadius),
            _ => Box(SceneMarker.BodyRadius),
        };

        private static Vector3[] Sun(in Vector3 shines)
        {
            var lines = new List<Vector3>();
            var radius = SceneMarker.BodyRadius;

            var forward = shines.LengthSquared() > 1e-8f ? Vector3.Normalize(shines) : -Vector3.UnitZ;
            var (right, up) = Basis(forward);

            for (var i = 0; i < RingSegments; i++)
            {
                lines.Add(OnCircle(right, up, radius, i, RingSegments));
                lines.Add(OnCircle(right, up, radius, i + 1, RingSegments));
            }

            for (var i = 0; i < RayCount; i++)
            {
                lines.Add(OnCircle(right, up, radius * 1.3f, i, RayCount));
                lines.Add(OnCircle(right, up, radius * 2f, i, RayCount));
            }

            lines.Add(forward * radius);
            lines.Add(forward * (radius + SceneMarker.DirectionalDistance * 0.35f));

            return [.. lines];
        }

        private static Vector3[] Globe(float radius)
        {
            var lines = new List<Vector3>();

            foreach (var (right, up) in new[]
            {
                (Vector3.UnitX, Vector3.UnitY),
                (Vector3.UnitY, Vector3.UnitZ),
                (Vector3.UnitZ, Vector3.UnitX),
            })
            {
                for (var i = 0; i < RingSegments; i++)
                {
                    lines.Add(OnCircle(right, up, radius, i, RingSegments));
                    lines.Add(OnCircle(right, up, radius, i + 1, RingSegments));
                }
            }

            return [.. lines];
        }

        private static Vector3[] Box(float radius)
        {
            Vector3[] corners =
            [
                new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
                new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
            ];

            (int From, int To)[] edges =
            [
                (0, 1), (1, 2), (2, 3), (3, 0),
                (4, 5), (5, 6), (6, 7), (7, 4),
                (0, 4), (1, 5), (2, 6), (3, 7),
            ];

            var lines = new List<Vector3>();

            foreach (var (from, to) in edges)
            {
                lines.Add(corners[from] * radius);
                lines.Add(corners[to] * radius);
            }

            return [.. lines];
        }

        private static Vector3 OnCircle(in Vector3 right, in Vector3 up, float radius, int step, int count)
        {
            var angle = MathF.Tau * step / count;

            return (right * MathF.Cos(angle) + up * MathF.Sin(angle)) * radius;
        }

        private static (Vector3 Right, Vector3 Up) Basis(in Vector3 forward)
        {
            var reference = MathF.Abs(forward.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;
            var right = Vector3.Normalize(Vector3.Cross(reference, forward));

            return (right, Vector3.Cross(forward, right));
        }

        private sealed class MarkerResources(ID3D11Device device) : IDisposable
        {
            private readonly RenderPipeline<TransformConstants> pipeline = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            private readonly Dictionary<(SceneMarker Shape, bool IsSelected), LineMesh> bodies = [];

            private LineMesh? reach;

            public void Draw(in Render3DContext render, in SceneMarker marker, bool isSelected)
            {
                Draw(render, Body(marker, isSelected), marker.Position, 1f);

                if (marker.Kind != MarkerKind.PointLight || marker.Reach <= 0f)
                    return;

                Draw(render, Reach(), marker.Position, marker.Reach);
            }

            private void Draw(in Render3DContext render, LineMesh mesh, in Vector3 position, float scale)
            {
                var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);

                var constants = TransformConstants.CreateUnlit(render.GetWorldViewProjection(world), 1f);

                pipeline.Draw(render.Context, constants, new DrawSettings(), mesh);
            }

            private LineMesh Body(in SceneMarker marker, bool isSelected)
            {
                var key = (marker with { Position = Vector3.Zero, Reach = 0f }, isSelected);

                if (bodies.TryGetValue(key, out var found))
                    return found;

                if (bodies.Count > 16)
                {
                    foreach (var mesh in bodies.Values)
                        mesh.Dispose();

                    bodies.Clear();
                }

                var color = marker.Kind == MarkerKind.Environment ? EnvironmentColor : LightColor;

                return bodies[key] = new LineMesh(
                    device, BuildOutline(key.Item1), isSelected ? color : Dim(color));
            }

            private static Color4 Dim(in Color4 color) => new(color.R * 0.65f, color.G * 0.65f, color.B * 0.65f, 1f);

            private LineMesh Reach() => reach ??= new LineMesh(device, Globe(1f), ReachColor);

            public void Dispose()
            {
                foreach (var mesh in bodies.Values)
                    mesh.Dispose();

                bodies.Clear();
                reach?.Dispose();
                reach = null;
                pipeline.Dispose();
            }
        }
    }
}
