using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Camera;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;

namespace YMM43D.PreviewTool.Rendering
{
    /// <summary>
    /// シーンを撮っているカメラの位置と、写る範囲を線で描きます。
    /// </summary>
    /// <remarks>
    /// レンズ側の枠は、動画の画面と同じ縦横比・同じ画角で開きます。枠に入っている
    /// ものがそのまま映るので、構図をここで決められます。
    /// </remarks>
    internal sealed class CameraGizmoRenderer : IDisposable
    {
        private static readonly Color4 GizmoColor = new(1f, 0.6f, 0f, 1f);

        /// <summary>写る範囲の枠を描く距離。カメラの本体より十分前に置く。</summary>
        private const float FrustumDepth = 1.2f;

        private readonly DeviceResourceCache<GizmoResources> resources;

        public CameraGizmoRenderer()
        {
            resources = new DeviceResourceCache<GizmoResources>(device => new GizmoResources(device));
        }

        /// <summary>
        /// カメラを描きます。
        /// </summary>
        /// <param name="pose">カメラの姿勢。</param>
        /// <param name="tangent">
        /// 画面の右端・下端が視線からどれだけ傾いているか（正接）。写る範囲がこれで決まります。
        /// </param>
        public void Draw(in Render3DContext render, in CameraPose pose, Vector2 tangent)
        {
            var constants = TransformConstants.Create(render.GetWorldViewProjection(pose.WorldMatrix), 1f);

            resources.Get(render.Device).Draw(render.Context, constants, tangent);
        }

        /// <summary>
        /// カメラの形。原点がレンズの位置で、-Z の方向を向いています。
        /// </summary>
        /// <remarks>
        /// 写る範囲そのものである四角錐と、上がどちらかを示す三角形だけで描きます。
        /// 箱の本体を足すと、画角によって四角錐の開き方が変わるぶん、本体との
        /// つながりが不格好になります。
        /// </remarks>
        private static Vector3[] BuildOutline(Vector2 tangent)
        {
            // 写る範囲の枠。傾き × 距離が、その距離での半分の広さになる。
            var half = tangent * FrustumDepth;

            Vector3[] frame =
            [
                new(-half.X,  half.Y, -FrustumDepth), new( half.X,  half.Y, -FrustumDepth),
                new( half.X, -half.Y, -FrustumDepth), new(-half.X, -half.Y, -FrustumDepth),
            ];

            var lines = new List<Vector3>();
            void Edge(Vector3 a, Vector3 b) { lines.Add(a); lines.Add(b); }

            // レンズの位置から枠の四隅へ。これが実際に映る四角錐になる。
            foreach (var corner in frame)
                Edge(Vector3.Zero, corner);

            for (var i = 0; i < 4; i++)
                Edge(frame[i], frame[(i + 1) % 4]);

            // 枠の上辺に載せた三角形。傾きを付けたときに、どちらが上か分かる。
            var peak = new Vector3(0f, half.Y * 1.4f, -FrustumDepth);
            Edge(frame[0], peak);
            Edge(frame[1], peak);

            return [.. lines];
        }

        public void Dispose() => resources.Dispose();

        /// <summary>
        /// デバイス1つ分の資源。形は写る範囲が変わったときだけ作り直します。
        /// </summary>
        private sealed class GizmoResources(ID3D11Device device) : IDisposable
        {
            private readonly RenderPipeline<TransformConstants> pipeline = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            private LineMesh? mesh;
            private Vector2 builtTangent;

            public void Draw(ID3D11DeviceContext context, in TransformConstants constants, Vector2 tangent)
            {
                if (mesh is null || builtTangent != tangent)
                {
                    mesh?.Dispose();
                    builtTangent = tangent;
                    mesh = new LineMesh(device, BuildOutline(tangent), GizmoColor);
                }

                pipeline.Draw(context, constants, new DrawSettings(), mesh);
            }

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                pipeline.Dispose();
            }
        }
    }
}
