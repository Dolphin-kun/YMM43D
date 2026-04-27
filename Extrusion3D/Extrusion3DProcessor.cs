using System;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Effects;

namespace Extrusion3D
{
    public class Extrusion3DProcessor(Extrusion3DEffect effect, IGraphicsDevicesAndContext devices) : IVideoEffectProcessor, I3DTextureProvider
    {
        private readonly Extrusion3DEffect effect = effect;
        private readonly IGraphicsDevicesAndContext devices = devices;
        private ID2D1Image? input;
        private ID3D11ShaderResourceView? srv;
        private D3D11RenderSurface? rasterizerSurface;

        public ID2D1Image Output => input ?? throw new NullReferenceException(nameof(input) + " is null");
        public System.Numerics.Vector2 TextureSize { get; private set; }

        public DrawDescription Update(EffectDescription effectDescription)
        {
            return effectDescription.DrawDescription;
        }

        /// <summary>
        /// 2Dエンジンのスレッドから呼ばれる。D2D の描画はここで完結させる。
        /// </summary>
        public void SetInput(ID2D1Image? input)
        {
            this.input = input;

            if (input == null) return;

            // 前回の SRV を破棄し新しいものを生成する
            srv?.Dispose();
            srv = null;
            TextureSize = default;

            // --- ここは2Dスレッド上なので DeviceContext を安全に使用できる ---
            var d2dDc = devices.DeviceContext;
            var bounds = d2dDc.GetImageLocalBounds(input);
            float imageWidth = bounds.Right - bounds.Left;
            float imageHeight = bounds.Bottom - bounds.Top;
            if (imageWidth <= 0 || imageHeight <= 0) return;

            TextureSize = new System.Numerics.Vector2(imageWidth, imageHeight);

            srv = D3D11Helper.CreateSrvFromD2DImage(SharedGraphics.IndependentDevice, input);
            if (srv != null) return;

            rasterizerSurface ??= new D3D11RenderSurface();
            rasterizerSurface.Recreate(devices, (int)imageWidth, (int)imageHeight);

            var oldTarget = d2dDc.Target;
            d2dDc.Target = rasterizerSurface.Bitmap;
            d2dDc.BeginDraw();
            d2dDc.Clear(new Vortice.Mathematics.Color4(0, 0, 0, 0));
            d2dDc.DrawImage(input, new System.Numerics.Vector2(-bounds.Left, -bounds.Top), null, InterpolationMode.Linear, CompositeMode.SourceOver);
            d2dDc.EndDraw();
            d2dDc.Target = oldTarget;

            srv = SharedGraphics.IndependentDevice.CreateShaderResourceView(rasterizerSurface.RenderTarget!);
        }

        public void ClearInput()
        {
            input = null;
        }

        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            return srv;
        }

        public void Dispose()
        {
            srv?.Dispose();
            rasterizerSurface?.Dispose();
        }
    }
}
