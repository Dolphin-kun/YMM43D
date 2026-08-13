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
        /// <summary>1×1 の板の四隅。範囲を出すのに使う。</summary>
        private static readonly Vector3[] Corners =
        [
            new(-0.5f, -0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
        ];

        private readonly Flat3DEffect effect;
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines;

        public Flat3DProcessor(Flat3DEffect effect, IGraphicsDevicesAndContext devices)
            : base(effect, devices)
        {
            this.effect = effect;

            pipelines = new DeviceResourceCache<RenderPipeline<TransformConstants>>(
                device => new RenderPipeline<TransformConstants>(
                    device,
                    new PlaneMesh(device),
                    new TextureMaterial(device)));
        }

        /// <remarks>
        /// 実寸は自分で掛けます。呼び出し側に任せると、実寸を掛けた後に回すことになり、
        /// 縦横で倍率が違うぶん板が歪みます。
        /// </remarks>
        public override bool ScalesToInputSize => false;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            var world = GetLocalMatrix(time) * item.World;

            var constants = TransformConstants.Create(
                render.GetWorldViewProjection(world), item.Opacity);

            // 板は裏返っても見えてほしいので両面描画する。
            // 深度は既定では書き込まない。半透明な板が透明部分まで深度を持つと、
            // 後ろの板が抜けて見えなくなり、同じ平面に並ぶとちらつく。
            var settings = item.ToDrawSettings(FaceCulling.None, texture) with
            {
                SkipDepthWrite = !effect.WritesDepth,
            };

            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        /// <summary>
        /// 1×1 の板を、実寸に広げてから回し、絵の中心へ動かす変換。
        /// </summary>
        /// <remarks>
        /// 絵の中心はアイテムの原点と一致するとは限りません。文字揃えやトリミングで
        /// 偏るので、そのずれは回した後に足します。
        /// </remarks>
        private Matrix4x4 GetLocalMatrix(in FrameContext time)
        {
            // YMM4 の回転は時計回りが正、3D 空間は反時計回りなので符号を反転する。
            var rotation = Rotation3D.ForObject(
                -effect.RotationX.GetFloat(time),
                -effect.RotationY.GetFloat(time),
                -effect.RotationZ.GetFloat(time));

            if (!TryGetSize(out var size, out var offset))
                return rotation;

            return WorldScale.CreateSizeMatrix(size, offset + size / 2f, rotation);
        }

        /// <remarks>
        /// 実寸も回転も自分で掛けるので、範囲もそれを通した後の形で答えます。
        /// 真横を向いて厚みが無くなった板は、範囲が潰れてそのまま描かれなくなります。
        /// </remarks>
        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
            => WorldBounds.FromPoints(Corners, GetLocalMatrix(itemTime));

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
