using System.Numerics;
using System.Reflection;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Plugin
{
    // 板をどう動かすかだけが違う変形エフェクトの、共通の土台。
    //
    // 派生側が用意するのは4つだけ。
    //   ShaderName    どの hlsl を使うか
    //   GetGrid       板を何マスに割るか
    //   GetConstants  シェーダーへ渡す値（b1）
    //   GetExtent     動いた先がどこまで届くか
    public abstract class Deform3DProcessorBase<TConstants>
        : VideoEffect3DProcessorBase where TConstants : unmanaged
    {
        private readonly DeviceResourceCache<DeformResources> resources;

        protected Deform3DProcessorBase(VideoEffect3DBase owner, IGraphicsDevicesAndContext devices)
            : base(owner, devices)
        {
            var assembly = GetType().Assembly;

            resources = new DeviceResourceCache<DeformResources>(
                device => new DeformResources(device, assembly, ShaderName));
        }

        protected abstract string ShaderName { get; }

        protected abstract bool IsUnlit { get; }

        protected abstract DeformGrid GetGrid(in FrameContext time);

        protected abstract TConstants GetConstants(in FrameContext time);

        protected abstract DeformExtent GetExtent(in FrameContext time);

        // 板の奥行きは、画像の大きさに合わせて拡大する。そうしないと、
        // 同じ「曲げ 90 度」でも、大きい画像ほど平べったく見えてしまう。
        private float DepthScale => TryGetSize(out var size, out _)
            ? WorldScale.ToWorld(size.X)
            : 1f;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            var world = Matrix4x4.CreateScale(1f, 1f, DepthScale) * item.World;

            var scene = render.CreateConstants(world, item, IsUnlit);
            var constants = GetConstants(time);

            var shared = resources.Get(render.Device);
            var settings = item.ToDrawSettings(FaceCulling.None, texture);

            shared.Pipeline.Draw(
                render.Context, scene, constants, settings, shared.GetMesh(GetGrid(time)));
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            var extent = GetExtent(itemTime);

            var half = 0.5f + MathF.Max(extent.Margin, 0f);
            var depth = MathF.Max(extent.Depth, 0f) * DepthScale;

            return new WorldBounds(
                new Vector3(-half, -half, -depth),
                new Vector3(half, half, depth));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed class DeformResources : IDisposable
        {
            private readonly ID3D11Device device;

            private DeformMesh? mesh;

            public RenderPipeline<TransformConstants> Pipeline { get; }

            public DeformResources(ID3D11Device device, Assembly assembly, string shader)
            {
                this.device = device;

                Pipeline = new RenderPipeline<TransformConstants>(
                    device, DeformVertex.InputElements, new DeformMaterial(device, assembly, shader));
            }

            public DeformMesh GetMesh(in DeformGrid grid)
            {
                if (mesh is { } existing && existing.Grid == grid)
                    return existing;

                mesh?.Dispose();

                return mesh = new DeformMesh(device, grid);
            }

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                Pipeline.Dispose();
            }
        }
    }

    // 変形したあと、板が元の四角からどれだけはみ出すか。
    // Margin は板の幅を 1 とした横縦のはみ出し、Depth は同じ物差しでの奥行き。
    public readonly record struct DeformExtent(float Margin, float Depth);
}
