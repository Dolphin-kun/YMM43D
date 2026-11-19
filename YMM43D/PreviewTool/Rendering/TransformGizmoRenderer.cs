using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Commons;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class TransformGizmoRenderer : IDisposable
    {
        private static readonly Color4 AxisXColor = new(1f, 0.25f, 0.3f, 1f);
        private static readonly Color4 AxisYColor = new(0.4f, 0.9f, 0.3f, 1f);
        private static readonly Color4 AxisZColor = new(0.3f, 0.55f, 1f, 1f);
        private static readonly Color4 CenterColor = new(0.9f, 0.9f, 0.9f, 1f);

        private const float IdleOpacity = 0.4f;

        private readonly DeviceResourceCache<GizmoResources> resources;

        public TransformGizmoRenderer()
        {
            resources = new DeviceResourceCache<GizmoResources>(device => new GizmoResources(device));
        }

        public void Draw(in Render3DContext render, in TransformGizmo gizmo, GizmoHandle active, GizmoStyle style, bool turnsInThreeAxes)
        {
            var world = Matrix4x4.CreateScale(gizmo.Scale) * gizmo.Orientation * Matrix4x4.CreateTranslation(gizmo.Origin);
            var transform = render.GetWorldViewProjection(world);

            var settings = new DrawSettings { IgnoreDepth = true };

            var shared = resources.Get(render.Device);

            foreach (var (handle, mesh) in shared.Parts)
            {
                if (!Shows(style, handle, turnsInThreeAxes))
                    continue;

                var opacity = active == GizmoHandle.None || active == handle ? 1f : IdleOpacity;

                shared.Pipeline.Draw(render.Context, TransformConstants.CreateUnlit(transform, opacity), settings, mesh);
            }
        }

        private static bool Shows(GizmoStyle style, GizmoHandle handle, bool turnsInThreeAxes) => style switch
        {
            GizmoStyle.Turn => TransformGizmo.IsTurn(handle)
                && (turnsInThreeAxes || handle is GizmoHandle.RotateZ),
            GizmoStyle.Scale => TransformGizmo.IsScale(handle) || handle is GizmoHandle.ScaleAll,
            _ => TransformGizmo.IsAxis(handle) || TransformGizmo.IsPlane(handle),
        };

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

        private static Vector3[] BuildScaleBar(Vector3 axis) => [Vector3.Zero, axis * (TransformGizmo.ScaleLength - TransformGizmo.ScaleBox / 2f)];

        private static SurfaceGeometry BuildScaleTip(Vector3 axis) => BuildBox(axis * TransformGizmo.ScaleLength, axis, TransformGizmo.ScaleBox / 2f);

        private static SurfaceGeometry BuildCenterBox() => BuildBox(Vector3.Zero, Vector3.UnitX, TransformGizmo.ScaleBox * 1.15f);

        private static SurfaceGeometry BuildBox(Vector3 center, Vector3 axis, float half)
        {
            var side = new Vector3(axis.Y, axis.Z, axis.X);
            var other = Vector3.Cross(axis, side);
            var corners = new Vector3[8];

            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = center
                    + axis * ((i & 1) == 0 ? -half : half)
                    + side * ((i & 2) == 0 ? -half : half)
                    + other * ((i & 4) == 0 ? -half : half);
            }

            return SurfaceGeometry.Faceted(corners, BoxFaces);
        }

        private static readonly int[][] BoxFaces =
        [
            [0, 2, 6, 4], [1, 5, 7, 3],
            [0, 4, 5, 1], [2, 3, 7, 6],
            [0, 1, 3, 2], [4, 6, 7, 5],
        ];

        private static Vector3[] BuildPlane(GizmoHandle handle)
        {
            var points = new List<Vector3>();

            for (var i = 0; i < TransformGizmo.PlaneCorners; i++)
            {
                points.Add(TransformGizmo.PlaneCorner(handle, i));
                points.Add(TransformGizmo.PlaneCorner(handle, i + 1));
            }

            return [.. points];
        }

        private static Vector3[] BuildRing(GizmoHandle handle)
        {
            var points = new List<Vector3>();

            for (var i = 0; i < TransformGizmo.RingSegments; i++)
            {
                points.Add(TransformGizmo.RingOffset(handle, i));
                points.Add(TransformGizmo.RingOffset(handle, i + 1));
            }

            return [.. points];
        }

        public void Dispose() => resources.Dispose();

        private sealed class GizmoResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; } = new RenderPipeline<TransformConstants>(
                    device, Vertex.InputElements, new VertexColorMaterial(device));

            public (GizmoHandle Handle, IMesh Mesh)[] Parts { get; } =
                [
                    (GizmoHandle.MoveX, new LineMesh(device, BuildArrow(Vector3.UnitX), AxisXColor)),
                    (GizmoHandle.MoveY, new LineMesh(device, BuildArrow(Vector3.UnitY), AxisYColor)),
                    (GizmoHandle.MoveZ, new LineMesh(device, BuildArrow(Vector3.UnitZ), AxisZColor)),
                    (GizmoHandle.PlaneYZ, new LineMesh(device, BuildPlane(GizmoHandle.PlaneYZ), AxisXColor)),
                    (GizmoHandle.PlaneZX, new LineMesh(device, BuildPlane(GizmoHandle.PlaneZX), AxisYColor)),
                    (GizmoHandle.PlaneXY, new LineMesh(device, BuildPlane(GizmoHandle.PlaneXY), AxisZColor)),
                    (GizmoHandle.RotateX, new LineMesh(device, BuildRing(GizmoHandle.RotateX), AxisXColor)),
                    (GizmoHandle.RotateY, new LineMesh(device, BuildRing(GizmoHandle.RotateY), AxisYColor)),
                    (GizmoHandle.RotateZ, new LineMesh(device, BuildRing(GizmoHandle.RotateZ), AxisZColor)),
                    (GizmoHandle.ScaleX, new LineMesh(device, BuildScaleBar(Vector3.UnitX), AxisXColor)),
                    (GizmoHandle.ScaleX, new SurfaceMesh(device, BuildScaleTip(Vector3.UnitX), [AxisXColor])),
                    (GizmoHandle.ScaleY, new LineMesh(device, BuildScaleBar(Vector3.UnitY), AxisYColor)),
                    (GizmoHandle.ScaleY, new SurfaceMesh(device, BuildScaleTip(Vector3.UnitY), [AxisYColor])),
                    (GizmoHandle.ScaleZ, new LineMesh(device, BuildScaleBar(Vector3.UnitZ), AxisZColor)),
                    (GizmoHandle.ScaleZ, new SurfaceMesh(device, BuildScaleTip(Vector3.UnitZ), [AxisZColor])),
                    (GizmoHandle.ScaleAll, new SurfaceMesh(device, BuildCenterBox(), [CenterColor])),
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
