using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Meshes;
using YMM43D.Integration;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace Extrusion3D
{
    /// <summary>
    /// 立体化エフェクトの描画処理。
    /// </summary>
    /// <remarks>
    /// このエフェクトは 2D の出力を変えず（<see cref="Output"/> は入力をそのまま返す）、
    /// 3D空間での描画だけを担当します。
    /// </remarks>
    internal sealed class Extrusion3DProcessor : IVideoEffectProcessor, I3DVideoEffect, I3DSizeProvider
    {
        private readonly Extrusion3DEffect effect;
        private readonly IGraphicsDevicesAndContext devices;
        private readonly D2DTextureBridge textureBridge = new();
        private readonly DeviceResourceCache<RenderPipeline<ExtrusionConstants>> pipelines;

        private ID2D1Image? input;
        private Vector2 inputSizeInDips;
        private Vector2 inputOffset;

        public Extrusion3DProcessor(Extrusion3DEffect effect, IGraphicsDevicesAndContext devices)
        {
            this.effect = effect;
            this.devices = devices;

            pipelines = new DeviceResourceCache<RenderPipeline<ExtrusionConstants>>(
                device => new RenderPipeline<ExtrusionConstants>(
                    device,
                    BoxMesh.CreateExtrusionBox(device),
                    new ExtrusionMaterial(device)));
        }

        public ID2D1Image Output => input ?? throw new InvalidOperationException("入力画像が設定されていません。");

        /// <summary>直近の <see cref="Update"/> で受け取った描画要求。</summary>
        public EffectDescription? EffectDescription { get; private set; }

        public bool RequiresMappedTexture => false;

        public DrawDescription Update(EffectDescription effectDescription)
        {
            EffectDescription = effectDescription;
            return effectDescription.DrawDescription;
        }

        public void SetInput(ID2D1Image? input) => this.input = input;

        public void ClearInput() => input = null;

        /// <summary>
        /// 入力画像を 3D 描画用デバイスのテクスチャとして取得します。
        /// </summary>
        /// <remarks>
        /// 呼ばれるたびに最新の入力内容を焼き直します。以前は入力が差し替わった
        /// タイミングで本体デバイス上のテクスチャに退避し、さらにプレビュー用
        /// デバイスへコピーする二段構えだったが、共有テクスチャに直接描き込めば
        /// 一段で済むうえ、内容が古くなることもない。
        /// </remarks>
        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            if (input is null)
                return null;

            var texture = textureBridge.GetTexture(device, devices, input, this, out var bounds);
            if (texture is not null)
            {
                inputSizeInDips = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                inputOffset = new Vector2(bounds.Left, bounds.Top);
            }

            return texture;
        }

        public bool TryGetSize(out Vector2 size, out Vector2 offset)
        {
            size = inputSizeInDips;
            offset = inputOffset;
            return size.X > 0 && size.Y > 0;
        }

        public void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            // エフェクトのパラメータはアイテム内の時間で評価する。描画要求が
            // 届いていればそちらを優先し、無ければ呼び出し元の時間を使う。
            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            var thickness = effect.Thickness.GetFloat(time) / 100f;
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
            var settings = item.ToDrawSettings(FaceCulling.Front, texture) with { Blend = YMM43D.Graphics.BlendMode.Normal };
            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        public void Dispose()
        {
            effect.DetachProcessor(this);
            pipelines.Dispose();
            textureBridge.Dispose();
            input = null;
        }
    }
}
