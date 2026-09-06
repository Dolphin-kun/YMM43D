using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Player;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class MarkerRenderer : IDisposable
    {
        private static readonly Color4 LightColor = new(1f, 0.84f, 0.31f, 1f);

        private static readonly Color4 ReachColor = new(0.65f, 0.55f, 0.22f, 1f);

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

        private static Vector3[] BuildOutline(MarkerKind kind) => kind switch
        {
            MarkerKind.DirectionalLight => Sun(),
            MarkerKind.PointLight => Globe(SceneMarker.BodyRadius),
            MarkerKind.SpotLight => Cone(),
            _ => [],
        };

        // 光の向きを +Z としたときの形。実際の向きは行列で合わせる。
        private static Vector3[] Sun()
        {
            var lines = new List<Vector3>();
            var radius = SceneMarker.BodyRadius;

            for (var i = 0; i < RingSegments; i++)
            {
                lines.Add(OnCircle(radius, i, RingSegments));
                lines.Add(OnCircle(radius, i + 1, RingSegments));
            }

            for (var i = 0; i < RayCount; i++)
            {
                lines.Add(OnCircle(radius * 1.3f, i, RayCount));
                lines.Add(OnCircle(radius * 2f, i, RayCount));
            }

            lines.Add(Vector3.UnitZ * radius);
            lines.Add(Vector3.UnitZ * (radius + SceneMarker.DirectionalDistance * 0.35f));

            return [.. lines];
        }

        // 先端が原点、+Z へ長さ 1・半径 1 の円錐。広がりと届く距離は行列で与える。
        private static Vector3[] Cone()
        {
            var lines = new List<Vector3>();

            for (var i = 0; i < RingSegments; i++)
            {
                lines.Add(OnCircle(1f, i, RingSegments) + Vector3.UnitZ);
                lines.Add(OnCircle(1f, i + 1, RingSegments) + Vector3.UnitZ);
            }

            for (var i = 0; i < RayCount; i++)
            {
                lines.Add(Vector3.Zero);
                lines.Add(OnCircle(1f, i, RayCount) + Vector3.UnitZ);
            }

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

        private static Vector3 OnCircle(float radius, int step, int count)
            => OnCircle(Vector3.UnitX, Vector3.UnitY, radius, step, count);

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

        // ローカルの +Z が渡した向きを指すようにする回転。
        private static Matrix4x4 Aim(in Vector3 direction)
        {
            if (direction.LengthSquared() < 1e-8f)
                return Matrix4x4.Identity;

            var forward = Vector3.Normalize(direction);
            var (right, up) = Basis(forward);

            return new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                forward.X, forward.Y, forward.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        private static Matrix4x4 WorldFor(in SceneMarker marker)
        {
            if (marker.Kind != MarkerKind.SpotLight)
                return Aim(marker.Direction) * Matrix4x4.CreateTranslation(marker.Position);

            var length = MathF.Max(marker.Reach, 0.01f);
            var spread = Math.Clamp(marker.Spread, SceneLight.MinSpread, SceneLight.MaxSpread);
            var radius = length * MathF.Tan(Rotation3D.ToRadians(spread));

            return Matrix4x4.CreateScale(radius, radius, length)
                * Aim(marker.Direction)
                * Matrix4x4.CreateTranslation(marker.Position);
        }

        private sealed class MarkerResources(ID3D11Device device) : IDisposable
        {
            private readonly RenderPipeline<TransformConstants> pipeline = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            private readonly Dictionary<(MarkerKind Kind, bool IsSelected), LineMesh> bodies = [];

            private LineMesh? reach;

            public void Draw(in Render3DContext render, in SceneMarker marker, bool isSelected)
            {
                if (Body(marker.Kind, isSelected) is { } body)
                    Draw(render, body, WorldFor(marker));

                if (marker.Kind != MarkerKind.PointLight || marker.Reach <= 0f)
                    return;

                Draw(
                    render,
                    Reach(),
                    Matrix4x4.CreateScale(marker.Reach) * Matrix4x4.CreateTranslation(marker.Position));
            }

            private void Draw(in Render3DContext render, LineMesh mesh, in Matrix4x4 world)
            {
                var constants = TransformConstants.CreateUnlit(render.GetWorldViewProjection(world), 1f);

                pipeline.Draw(render.Context, constants, new DrawSettings(), mesh);
            }

            private LineMesh? Body(MarkerKind kind, bool isSelected)
            {
                var key = (kind, isSelected);

                if (bodies.TryGetValue(key, out var found))
                    return found;

                var outline = BuildOutline(kind);

                if (outline.Length == 0)
                    return null;

                return bodies[key] = new LineMesh(
                    device, outline, isSelected ? LightColor : Dim(LightColor));
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
