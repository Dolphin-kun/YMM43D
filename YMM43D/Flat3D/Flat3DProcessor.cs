using System.Numerics;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Flat3D
{
    internal sealed class Flat3DProcessor : VideoEffect3DProcessorBase
    {
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines;

        public Flat3DProcessor(Flat3DEffect effect, IGraphicsDevicesAndContext devices)
            : base(effect, devices)
        {
            pipelines = new DeviceResourceCache<RenderPipeline<TransformConstants>>(
                device => new RenderPipeline<TransformConstants>(
                    device,
                    new PlaneMesh(device),
                    new TextureMaterial(device)));
        }

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            var constants = TransformConstants.Create(
                render.GetWorldViewProjection(item.World), item.Opacity);

            // 板は裏返っても見えてほしいので両面描画する。深度は書き込まない。
            // 半透明な板が透明部分まで深度を持つと、後ろの板が抜けて見えなくなり、
            // 同じ平面に並ぶとちらつく。3Dプレビュー側の板と揃えてある。
            var settings = item.ToDrawSettings(FaceCulling.None, texture) with { SkipDepthWrite = true };

            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        /// <remarks>
        /// 厚みは持たない。範囲は入力画像の実寸に広げられるので、ここでは 1×1 の板。
        /// </remarks>
        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
            => new(new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f));

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
