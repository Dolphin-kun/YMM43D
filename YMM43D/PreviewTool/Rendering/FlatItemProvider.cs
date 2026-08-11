using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;

namespace YMM43D.PreviewTool.Rendering
{
    /// <summary>
    /// 3D対応していない普通のアイテムを、板にテクスチャを貼った形で 3D 空間に表示します。
    /// </summary>
    /// <remarks>
    /// 独自の <see cref="I3DProvider"/> を持たないアイテムすべてに使われる既定の描画方法です。
    /// </remarks>
    internal sealed class FlatItemProvider : I3DProvider, IDisposable
    {
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines;

        /// <summary>アイテムの見た目そのものを貼るため、必ず画像を要求します。</summary>
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
            if (item.Texture is null)
                return;

            var constants = TransformConstants.Create(
                render.GetWorldViewProjection(item.World), item.Opacity);

            // 板は裏返っても見えてほしいので両面描画する。
            // 深度は書き込まない。半透明な板が透明部分まで深度を持つと、
            // 後ろの板が抜けて見えなくなり、同じ平面に並ぶとちらつくため。
            var settings = item.ToDrawSettings(FaceCulling.None) with { SkipDepthWrite = true };

            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        public void Dispose() => pipelines.Dispose();
    }
}
