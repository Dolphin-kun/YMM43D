using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool.Rendering
{
    // 骨を Blender と同じ八面体の形で出す。手前の物に隠れないように、奥行きを見ないで描く。
    internal sealed class BoneRenderer : IDisposable
    {
        private static readonly Color4 BoneColor = new(0.45f, 0.75f, 1f, 1f);

        private static readonly Color4 SelectedColor = new(1f, 0.85f, 0.3f, 1f);

        // 頭から尻尾までの長さに対する、いちばん太いところの太さと、その位置。
        private const float Thickness = 0.1f;

        private readonly DeviceResourceCache<BoneResources> resources;

        public BoneRenderer()
        {
            resources = new DeviceResourceCache<BoneResources>(device => new BoneResources(device));
        }

        public void Draw(in Render3DContext render, IReadOnlyList<PreviewBone> bones, int selected)
        {
            if (bones.Count == 0)
                return;

            var shared = resources.Get(render.Device);

            for (var i = 0; i < bones.Count; i++)
                shared.DrawBone(render, bones[i].Position, bones[i].Tail, i == selected);
        }

        public void Dispose() => resources.Dispose();

        // Blender の骨と同じ形。頭から少し進んだところに四角い輪があり、そこから尻尾へすぼまる。
        private static Vector3[] Body()
        {
            var head = Vector3.Zero;
            var tail = Vector3.UnitZ;

            Vector3[] ring =
            [
                new(Thickness, 0f, Thickness),
                new(0f, Thickness, Thickness),
                new(-Thickness, 0f, Thickness),
                new(0f, -Thickness, Thickness),
            ];

            var points = new List<Vector3>();

            for (var i = 0; i < ring.Length; i++)
            {
                points.Add(head);
                points.Add(ring[i]);

                points.Add(ring[i]);
                points.Add(ring[(i + 1) % ring.Length]);

                points.Add(ring[i]);
                points.Add(tail);
            }

            return [.. points];
        }

        private sealed class BoneResources(ID3D11Device device) : IDisposable
        {
            private readonly RenderPipeline<TransformConstants> pipeline = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            private readonly Dictionary<bool, LineMesh> bodies = [];

            public void DrawBone(
                in Render3DContext render, in Vector3 head, in Vector3 tail, bool isSelected)
            {
                var direction = tail - head;
                var length = direction.Length();

                if (length < 1e-6f)
                    return;

                var world = Matrix4x4.CreateScale(length) * Aim(direction / length)
                          * Matrix4x4.CreateTranslation(head);

                Draw(render, Mesh(bodies, isSelected, Body()), world);
            }

            private void Draw(in Render3DContext render, LineMesh mesh, in Matrix4x4 world)
            {
                var constants = TransformConstants.CreateUnlit(render.GetWorldViewProjection(world), 1f);

                pipeline.Draw(render.Context, constants, new DrawSettings { IgnoreDepth = true }, mesh);
            }

            private LineMesh Mesh(Dictionary<bool, LineMesh> cache, bool isSelected, Vector3[] points)
            {
                if (cache.TryGetValue(isSelected, out var found))
                    return found;

                return cache[isSelected] = new LineMesh(
                    device, points, isSelected ? SelectedColor : BoneColor);
            }

            private static Matrix4x4 Aim(in Vector3 forward)
            {
                var reference = MathF.Abs(forward.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;
                var right = Vector3.Normalize(Vector3.Cross(reference, forward));
                var up = Vector3.Cross(forward, right);

                return new Matrix4x4(
                    right.X, right.Y, right.Z, 0f,
                    up.X, up.Y, up.Z, 0f,
                    forward.X, forward.Y, forward.Z, 0f,
                    0f, 0f, 0f, 1f);
            }

            public void Dispose()
            {
                foreach (var mesh in bodies.Values)
                    mesh.Dispose();

                bodies.Clear();
                pipeline.Dispose();
            }
        }
    }
}
