using System.Numerics;
using YMM43D.Graphics;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;

namespace Extrusion3D
{
    internal sealed class Extrusion3DProcessor : VideoEffect3DProcessorBase
    {
        private readonly Extrusion3DEffect effect;
        private readonly DeviceResourceCache<RenderPipeline<ExtrusionConstants>> pipelines;

        public Extrusion3DProcessor(Extrusion3DEffect effect, IGraphicsDevicesAndContext devices)
            : base(effect, devices)
        {
            this.effect = effect;

            pipelines = new DeviceResourceCache<RenderPipeline<ExtrusionConstants>>(
                device => new RenderPipeline<ExtrusionConstants>(
                    device,
                    BoxMesh.CreateExtrusionBox(device),
                    new ExtrusionMaterial(device)));
        }

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            // エフェクトのパラメータはアイテム内の時間で評価する。描画要求が
            // 届いていればそちらを優先し、無ければ呼び出し元の時間を使う。
            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            var thickness = GetThickness(time);
            if (thickness <= 0)
                return;

            var world = Matrix4x4.CreateScale(1f, 1f, thickness) * item.World;

            // レイマーチングはボックスのローカル座標系で行うため、
            // カメラ位置もその座標系に持ち込む。
            Matrix4x4.Invert(world, out var inverseWorld);
            var cameraLocalPos = Vector3.Transform(render.GetCameraPosition(), inverseWorld);

            var sideColor = effect.SideColor;
            var constants = new ExtrusionConstants
            {
                WorldViewProjection = Matrix4x4.Transpose(render.GetWorldViewProjection(world)),
                SideColor = new Vector4(sideColor.R, sideColor.G, sideColor.B, sideColor.A) / 255f,
                CameraLocalPos = cameraLocalPos,
                Opacity = item.Opacity,
                ExtrusionType = (int)effect.ExtrusionType,
                Attenuation = effect.Attenuation.GetFloat(time) / 100f,
            };

            // ボックスの内側からレイを飛ばすため、手前の面ではなく奥の面を描く。
            var settings = item.ToDrawSettings(FaceCulling.Front, texture) with { Blend = BlendMode.Normal };
            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        private float GetThickness(in FrameContext time)
            => WorldScale.ToWorld(effect.Thickness.GetFloat(time));

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            var thickness = GetThickness(itemTime);
            if (thickness <= 0)
                return WorldBounds.Empty;

            // ワールド行列が入力画像の実寸を掛けてくれるので、ここでは 1×1 の板として
            // 答える。厚みだけは自分で掛けているぶん、そのまま奥行きになる。
            return new WorldBounds(
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, thickness));
        }

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
