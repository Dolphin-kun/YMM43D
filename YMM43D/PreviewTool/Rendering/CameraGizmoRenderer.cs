using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class CameraGizmoRenderer : IDisposable
    {
        private static readonly Color4 GizmoColor = new(1f, 0.6f, 0f, 1f);

        private readonly DeviceResourceCache<GizmoResources> resources;

        public CameraGizmoRenderer()
        {
            resources = new DeviceResourceCache<GizmoResources>(device => new GizmoResources(device));
        }

        public void Draw(in Render3DContext render, in CameraPose pose, Vector2 tangent)
        {
            var constants = TransformConstants.CreateUnlit(render.GetWorldViewProjection(pose.WorldMatrix), 1f);

            resources.Get(render.Device).Draw(render.Context, constants, tangent);
        }

        private static Vector3[] BuildOutline(Vector2 tangent)
        {
            var frame = CameraFrustum.LocalCorners(tangent);

            var lines = new List<Vector3>();
            void Edge(Vector3 a, Vector3 b) { lines.Add(a); lines.Add(b); }

            foreach (var corner in frame)
                Edge(Vector3.Zero, corner);

            for (var i = 0; i < 4; i++)
                Edge(frame[i], frame[(i + 1) % 4]);

            var peak = CameraFrustum.LocalPeak(tangent);
            Edge(frame[0], peak);
            Edge(frame[1], peak);

            return [.. lines];
        }

        public void Dispose() => resources.Dispose();

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
