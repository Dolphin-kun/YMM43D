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
    /// <summary>
    /// 掴んでいるアイテムに、動かす向きの案内を描きます。
    /// </summary>
    /// <remarks>
    /// 矢印を掴めばその軸だけに沿って動き、輪を掴めば画面と平行に回ります。
    /// 案内が無いと、動かしたい向き以外にも動いてしまいます。
    /// </remarks>
    internal sealed class TransformGizmoRenderer : IDisposable
    {
        private static readonly Color4 AxisXColor = new(1f, 0.25f, 0.3f, 1f);
        private static readonly Color4 AxisYColor = new(0.4f, 0.9f, 0.3f, 1f);
        private static readonly Color4 AxisZColor = new(0.3f, 0.55f, 1f, 1f);
        private static readonly Color4 RingColor = new(0.9f, 0.85f, 0.35f, 1f);

        /// <summary>掴んでいない部分の薄さ。掴んでいる部分だけがはっきり出る。</summary>
        private const float IdleOpacity = 0.4f;

        private readonly DeviceResourceCache<GizmoResources> resources;

        public TransformGizmoRenderer()
        {
            resources = new DeviceResourceCache<GizmoResources>(device => new GizmoResources(device));
        }

        /// <summary>
        /// 案内を描きます。
        /// </summary>
        /// <param name="gizmo">アイテムの位置と大きさ。</param>
        /// <param name="active">いま掴んでいる部分。<see cref="GizmoHandle.None"/> なら全部を薄く描きます。</param>
        public void Draw(in Render3DContext render, in TransformGizmo gizmo, GizmoHandle active)
        {
            // 形は原点まわりの一定の大きさで作っておき、置く場所と大きさは行列で与える。
            var world = Matrix4x4.CreateScale(gizmo.Scale) * Matrix4x4.CreateTranslation(gizmo.Origin);
            var transform = render.GetWorldViewProjection(world);

            // 他のアイテムに隠されると掴めているのか分からなくなる。深度は見ない。
            var settings = new DrawSettings { IgnoreDepth = true };

            var shared = resources.Get(render.Device);

            foreach (var (handle, mesh) in shared.Parts)
            {
                var opacity = active == GizmoHandle.None || active == handle ? 1f : IdleOpacity;

                shared.Pipeline.Draw(
                    render.Context, TransformConstants.Create(transform, opacity), settings, mesh);
            }
        }

        /// <summary>矢印1本。原点から軸の向きへ伸ばし、先に羽を付ける。</summary>
        private static Vector3[] BuildArrow(Vector3 axis)
        {
            // 羽を開く向きは、軸に垂直な2方向であればどれでもよい。
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

        /// <summary>Z 軸のまわりを回る輪。</summary>
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

        private sealed class GizmoResources : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; }

            /// <summary>掴める部分と、その形。</summary>
            public (GizmoHandle Handle, LineMesh Mesh)[] Parts { get; }

            public GizmoResources(ID3D11Device device)
            {
                Pipeline = new RenderPipeline<TransformConstants>(
                    device, Vertex.InputElements, new VertexColorMaterial(device));

                Parts =
                [
                    (GizmoHandle.MoveX, new LineMesh(device, BuildArrow(Vector3.UnitX), AxisXColor)),
                    (GizmoHandle.MoveY, new LineMesh(device, BuildArrow(Vector3.UnitY), AxisYColor)),
                    (GizmoHandle.MoveZ, new LineMesh(device, BuildArrow(Vector3.UnitZ), AxisZColor)),
                    (GizmoHandle.RotateZ, new LineMesh(device, BuildRing(), RingColor)),
                ];
            }

            public void Dispose()
            {
                foreach (var (_, mesh) in Parts)
                    mesh.Dispose();

                Pipeline.Dispose();
            }
        }
    }
}
