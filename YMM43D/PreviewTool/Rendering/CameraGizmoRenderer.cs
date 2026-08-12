using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Camera;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class CameraGizmoRenderer : IDisposable
    {
        private static readonly Color4 GizmoColor = new(1f, 0.6f, 0f, 1f);

        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines;

        public CameraGizmoRenderer()
        {
            pipelines = new DeviceResourceCache<RenderPipeline<TransformConstants>>(
                device => new RenderPipeline<TransformConstants>(
                    device,
                    new LineMesh(device, BuildOutline(), GizmoColor),
                    new VertexColorMaterial(device)));
        }

        public void Draw(in Render3DContext render, in CameraPose pose)
        {
            var constants = TransformConstants.Create(render.GetWorldViewProjection(pose.WorldMatrix), 1f);
            pipelines.Get(render.Device).Draw(render.Context, constants, new DrawSettings());
        }

        private static Vector3[] BuildOutline()
        {
            var bodyMin = new Vector3(-0.4f, -0.3f, 0.0f);
            var bodyMax = new Vector3(0.4f, 0.3f, 0.8f);

            // 本体の8頂点。手前の面 (0-3) が前方、奥の面 (4-7) が後方。
            Vector3[] body =
            [
                new(bodyMin.X, bodyMin.Y, bodyMin.Z), new(bodyMax.X, bodyMin.Y, bodyMin.Z),
                new(bodyMax.X, bodyMax.Y, bodyMin.Z), new(bodyMin.X, bodyMax.Y, bodyMin.Z),
                new(bodyMin.X, bodyMin.Y, bodyMax.Z), new(bodyMax.X, bodyMin.Y, bodyMax.Z),
                new(bodyMax.X, bodyMax.Y, bodyMax.Z), new(bodyMin.X, bodyMax.Y, bodyMax.Z),
            ];

            // レンズの先端。本体より大きく開いた四角形。
            Vector3[] lens =
            [
                new(-0.8f,  0.6f, -1.2f), new( 0.8f,  0.6f, -1.2f),
                new( 0.8f, -0.6f, -1.2f), new(-0.8f, -0.6f, -1.2f),
            ];

            var lines = new List<Vector3>();
            void Edge(Vector3 a, Vector3 b) { lines.Add(a); lines.Add(b); }
            void Loop(Vector3[] quad, int offset = 0)
            {
                for (var i = 0; i < 4; i++)
                    Edge(quad[offset + i], quad[offset + (i + 1) % 4]);
            }

            Loop(body);          // 本体の前面
            Loop(body, 4);       // 本体の背面
            for (var i = 0; i < 4; i++)
                Edge(body[i], body[i + 4]);   // 前面と背面をつなぐ稜線

            // 本体前面の四隅からレンズ先端へ。レンズ側は巻き方向が逆なので添字を反転する。
            for (var i = 0; i < 4; i++)
                Edge(body[i], lens[3 - i]);
            Loop(lens);          // レンズ先端の枠

            return [.. lines];
        }

        public void Dispose() => pipelines.Dispose();
    }
}
