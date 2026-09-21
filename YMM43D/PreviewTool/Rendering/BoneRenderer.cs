using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool.Rendering
{
    // 骨を線でつないで出す。手前の物に隠れないように、奥行きを見ないで描く。
    internal sealed class BoneRenderer : IDisposable
    {
        private static readonly Color4 BoneColor = new(0.45f, 0.75f, 1f, 1f);

        private static readonly Color4 SelectedColor = new(1f, 0.85f, 0.3f, 1f);

        // 骨の玉の大きさ。カメラからの距離に対する割合で決める。
        private const float KnobRatio = 0.005f;

        // 子を持たない骨（ねじれ用や飾りの骨）は、関節と見分けられるように小さく出す。
        private const float LeafRatio = 0.5f;

        private readonly DeviceResourceCache<BoneResources> resources;

        public BoneRenderer()
        {
            resources = new DeviceResourceCache<BoneResources>(device => new BoneResources(device));
        }

        public void Draw(
            in Render3DContext render,
            IReadOnlyList<PreviewBone> bones,
            in Vector3 cameraPosition,
            int selected)
        {
            if (bones.Count == 0)
                return;

            var shared = resources.Get(render.Device);

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                var isSelected = i == selected;

                if (bone.HasParent)
                    shared.DrawSegment(render, bone.ParentPosition, bone.Position, isSelected);

                var size = MathF.Max(0.01f, Vector3.Distance(bone.Position, cameraPosition) * KnobRatio)
                         * (bone.HasChildren ? 1f : LeafRatio);

                shared.DrawKnob(render, bone.Position, size, isSelected);
            }
        }

        public void Dispose() => resources.Dispose();

        private static Vector3[] Knob()
        {
            var points = new List<Vector3>();

            foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            {
                points.Add(-axis);
                points.Add(axis);
            }

            return [.. points];
        }

        private sealed class BoneResources(ID3D11Device device) : IDisposable
        {
            private readonly RenderPipeline<TransformConstants> pipeline = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            private readonly Dictionary<bool, LineMesh> segments = [];
            private readonly Dictionary<bool, LineMesh> knobs = [];

            public void DrawSegment(
                in Render3DContext render, in Vector3 from, in Vector3 to, bool isSelected)
            {
                var direction = to - from;
                var length = direction.Length();

                if (length < 1e-6f)
                    return;

                var world = Matrix4x4.CreateScale(length) * Aim(direction / length)
                          * Matrix4x4.CreateTranslation(from);

                Draw(render, Mesh(segments, isSelected, [Vector3.Zero, Vector3.UnitZ]), world);
            }

            public void DrawKnob(in Render3DContext render, in Vector3 at, float size, bool isSelected)
                => Draw(
                    render,
                    Mesh(knobs, isSelected, Knob()),
                    Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at));

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
                foreach (var mesh in segments.Values)
                    mesh.Dispose();

                foreach (var mesh in knobs.Values)
                    mesh.Dispose();

                segments.Clear();
                knobs.Clear();
                pipeline.Dispose();
            }
        }
    }
}
