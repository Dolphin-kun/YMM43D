using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class TransformGizmoRenderer : IDisposable
    {
        private static readonly Color4 AxisXColor = new(1f, 0.25f, 0.3f, 1f);
        private static readonly Color4 AxisYColor = new(0.4f, 0.9f, 0.3f, 1f);
        private static readonly Color4 AxisZColor = new(0.3f, 0.55f, 1f, 1f);
        private static readonly Color4 RingColor = new(0.9f, 0.85f, 0.35f, 1f);

        private const float IdleOpacity = 0.4f;

        private readonly DeviceResourceCache<GizmoResources> resources;

        public TransformGizmoRenderer()
        {
            resources = new DeviceResourceCache<GizmoResources>(device => new GizmoResources(device));
        }

        public void Draw(in Render3DContext render, in TransformGizmo gizmo, GizmoHandle active)
        {
            var world = Matrix4x4.CreateScale(gizmo.Scale) * Matrix4x4.CreateTranslation(gizmo.Origin);
            var transform = render.GetWorldViewProjection(world);

            var settings = new DrawSettings { IgnoreDepth = true };

            var shared = resources.Get(render.Device);

            foreach (var (handle, mesh) in shared.Parts)
            {
                var opacity = active == GizmoHandle.None || active == handle ? 1f : IdleOpacity;

                shared.Pipeline.Draw(
                    render.Context, TransformConstants.CreateUnlit(transform, opacity), settings, mesh);
            }
        }

        private static Vector3[] BuildArrow(Vector3 axis)
        {
            var side = new Vector3(axis.Y, axis.Z, axis.X);
            var other = Vector3.Cross(axis, side);

            var tip = axis * TransformGizmo.AxisLength;
            var neck = axis * (TransformGizmo.AxisLength - TransformGizmo.HeadLength);
            var spread = TransformGizmo.HeadLength * 0.45f;

            return
            [
                Vector3.Zero, tip,
                tip, neck + side * spread,
                tip, neck - side * spread,
                tip, neck + other * spread,
                tip, neck - other * spread,
            ];
        }

        private static Vector3[] BuildRing()
        {
            var points = new List<Vector3>();

            for (var i = 0; i < TransformGizmo.RingSegments; i++)
            {
                var from = MathF.Tau * i / TransformGizmo.RingSegments;
                var to = MathF.Tau * (i + 1) / TransformGizmo.RingSegments;

                points.Add(new Vector3(MathF.Cos(from), MathF.Sin(from), 0f) * TransformGizmo.RingRadius);
                points.Add(new Vector3(MathF.Cos(to), MathF.Sin(to), 0f) * TransformGizmo.RingRadius);
            }

            return [.. points];
        }

        public void Dispose() => resources.Dispose();

        private sealed class GizmoResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; } = new RenderPipeline<TransformConstants>(
                    device, Vertex.InputElements, new VertexColorMaterial(device));

            public (GizmoHandle Handle, LineMesh Mesh)[] Parts { get; } =
                [
                    (GizmoHandle.MoveX, new LineMesh(device, BuildArrow(Vector3.UnitX), AxisXColor)),
                    (GizmoHandle.MoveY, new LineMesh(device, BuildArrow(Vector3.UnitY), AxisYColor)),
                    (GizmoHandle.MoveZ, new LineMesh(device, BuildArrow(Vector3.UnitZ), AxisZColor)),
                    (GizmoHandle.RotateZ, new LineMesh(device, BuildRing(), RingColor)),
                ];

            public void Dispose()
            {
                foreach (var (_, mesh) in Parts)
                    mesh.Dispose();

                Pipeline.Dispose();
            }
        }
    }
}
