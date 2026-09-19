using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics;
using YMM43D.Commons;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class FlatItemProvider : I3DProvider, IDisposable
    {
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines;

        public bool RequiresMappedTexture => true;

        public FlatItemProvider()
        {
            pipelines = new DeviceResourceCache<RenderPipeline<TransformConstants>>(
                device => new RenderPipeline<TransformConstants>(
                    device,
                    new PlaneMesh(device),
                    new TextureMaterial(device)));
        }

        public void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (item.Texture is null || item.DepthOnly)
                return;

            // 板として描くのは YMM4 が 2D で描く絵（光は当たらない、または描いた時点で当たっている）なので、ここでは光を当てない。
            var constants = render.CreateConstants(item.World, item.Opacity, unlit: true);

            var settings = item.ToDrawSettings(FaceCulling.None) with { SkipDepthWrite = true };

            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        public void Dispose() => pipelines.Dispose();
    }
}
