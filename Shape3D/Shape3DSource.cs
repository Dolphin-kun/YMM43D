using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;

namespace Shape3D
{
    internal sealed class Shape3DSource : Shape3DSourceBase
    {
        private readonly Shape3DParameter parameter;
        private readonly DeviceResourceCache<SolidResources> resources;

        public Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter) : base(devices)
        {
            this.parameter = parameter;
            resources = new DeviceResourceCache<SolidResources>(device => new SolidResources(device));
        }

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var world = GetLocalMatrix(item.Time) * item.World;
            var constants = TransformConstants.Create(render.GetWorldViewProjection(world), item.Opacity);

            var shared = resources.Get(render.Device);
            var mesh = shared.GetMesh(parameter.Solid, parameter.ResolvedColors);

            // 半透明でも面の前後関係が正しく見えるよう、内側を向いた面を先に描いてから
            // 外側を向いた面を重ねる。
            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Front), mesh);
            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Back), mesh);
        }

        private float GetSize(in FrameContext itemTime)
            => WorldScale.ToWorld(parameter.Size.GetFloat(itemTime));

        private Matrix4x4 GetLocalMatrix(in FrameContext itemTime)
            => Matrix4x4.CreateScale(GetSize(itemTime))
             * Rotation3D.ForObject(
                   parameter.RotationX.GetFloat(itemTime),
                   parameter.RotationY.GetFloat(itemTime),
                   parameter.RotationZ.GetFloat(itemTime));

        // 頂点そのものを回して範囲を出す。外接立方体を回してから囲み直すと、
        // 立方体の隅につられて最大で √3 ≒ 1.73 倍まで膨らみ、アイテムの枠が
        // 見た目より大きくなる。立方体以外の形では隅がまるごと余る。
        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
            => WorldBounds.FromPoints(Polyhedron.Get(parameter.Solid).Vertices, GetLocalMatrix(itemTime));

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// デバイス1つ分の資源。形状は、形か色が変わったときだけ作り直します。
        /// </summary>
        private sealed class SolidResources(ID3D11Device device) : IDisposable
        {
            private PolyhedronMesh? mesh;
            private Color[] builtColors = [];

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            public PolyhedronMesh GetMesh(SolidKind kind, IReadOnlyList<Color> colors)
            {
                if (mesh is { } existing && existing.Kind == kind && builtColors.SequenceEqual(colors))
                    return existing;

                mesh?.Dispose();
                builtColors = [.. colors];

                return mesh = new PolyhedronMesh(device, kind, colors);
            }

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                Pipeline.Dispose();
            }
        }
    }
}
