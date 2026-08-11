using System;
using System.Numerics;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Effects;
using YMM43D.Commons;
using Math = System.Math;

namespace Extrusion3D
{
    public class Extrusion3DProcessor : IVideoEffectProcessor, I3DTextureProvider, I3DProvider, IDisposable
    {
        private readonly Extrusion3DEffect effect;
        private readonly IGraphicsDevicesAndContext devices;
        private ID2D1Image? input;
        private readonly Extrusion3DSource source;

        // メインデバイス上の退避用テクスチャ
        private ID3D11Texture2D? mainTexture;

        // プレビューデバイス用のキャッシュリソース
        private ID3D11Texture2D? previewTexture;
        private ID3D11ShaderResourceView? previewSrv;
        private IntPtr previewDevicePointer;

        public Extrusion3DProcessor(Extrusion3DEffect effect, IGraphicsDevicesAndContext devices)
        {
            this.effect = effect;
            this.devices = devices;
            this.source = new Extrusion3DSource(effect, this);
            effect.LastProvider = this;
        }

        public ID2D1Image Output => input ?? throw new NullReferenceException(nameof(input) + " is null");

        public EffectDescription? EffectDescription { get; private set; }
        public bool RequiresMappedTexture => false;

        public DrawDescription Update(EffectDescription effectDescription)
        {
            EffectDescription = effectDescription;
            return effectDescription.DrawDescription;
        }

        public void Draw(ID3D11Device device, ID3D11DeviceContext d3dDc, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            source.Draw3D(device, d3dDc, view, projection, drawContext);
        }

        public void SetInput(ID2D1Image? input)
        {
            this.input = input;
            
            mainTexture?.Dispose();
            mainTexture = null;
            ClearPreviewResources();

            if (input == null) return;

            var d2dContext = devices.DeviceContext;
            RawRectF bounds = D3D11Helper.GetImageBounds(d2dContext, input, out _, out _);
            int width = (int)Math.Max(1, Math.Ceiling(bounds.Right - bounds.Left));
            int height = (int)Math.Max(1, Math.Ceiling(bounds.Bottom - bounds.Top));

            int texWidth = width;
            int texHeight = height;

            var desc = new Texture2DDescription
            {
                Width = texWidth,
                Height = texHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.None
            };

            // メインデバイス上に退避用テクスチャを作成
            mainTexture = devices.D3D.Device.CreateTexture2D(desc);

            // デフォルトのDPI設定(96DPI)で targetBitmap を生成します。
            // これにより、d2dContext.Target 切り替え時にD2DがDPI整合を自動で行って描き込みます。
            using var surface = mainTexture.QueryInterface<Vortice.DXGI.IDXGISurface>();
            using var targetBitmap = d2dContext.CreateBitmapFromDxgiSurface(surface);

            // D3D11Helper.UpdateSharedTexture を用いて、同期的かつ安全に画像データを等倍（1:1）で描き写す
            D3D11Helper.UpdateSharedTexture(d2dContext, input, targetBitmap, bounds, devices.D3D.Device);
        }

        public void ClearInput()
        {
            input = null;
            mainTexture?.Dispose();
            mainTexture = null;
            ClearPreviewResources();
        }

        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            if (mainTexture == null) return null;

            // キャッシュされているプレビューデバイスが一致する場合は使い回す
            if (previewSrv != null && previewDevicePointer == device.NativePointer)
            {
                return previewSrv;
            }

            ClearPreviewResources();
            previewDevicePointer = device.NativePointer;

            // プレビューで渡されたデバイスがメインデバイスと同一の場合は、退避テクスチャから直接 SRV を作成
            if (device.NativePointer == devices.D3D.Device.NativePointer)
            {
                previewSrv = device.CreateShaderResourceView(mainTexture);
                return previewSrv;
            }

            // デバイスが異なる場合は、共有テクスチャを利用してデータをプレビューデバイスへ GPU コピーする
            var desc = mainTexture.Description;
            desc.MiscFlags = ResourceOptionFlags.Shared;

            // A. プレビュー用デバイスに共有テクスチャを作成
            previewTexture = device.CreateTexture2D(desc);
            previewSrv = device.CreateShaderResourceView(previewTexture);

            // B. プレビューデバイス of 共有ハンドルを取得
            using var dxgiResource = previewTexture.QueryInterface<Vortice.DXGI.IDXGIResource>();
            nint sharedHandle = dxgiResource.SharedHandle;

            // C. メインデバイス側で、その共有ハンドルを開く
            using var sharedTextureOnMain = devices.D3D.Device.OpenSharedResource<ID3D11Texture2D>(sharedHandle);

            // D. メインデバイス上で退避テクスチャから共有テクスチャへ高速コピーする
            lock (devices.D3D.Device)
            {
                devices.D3D.Device.ImmediateContext.CopyResource(sharedTextureOnMain, mainTexture);
                devices.D3D.Device.ImmediateContext.Flush();
            }

            return previewSrv;
        }

        private void ClearPreviewResources()
        {
            previewSrv?.Dispose();
            previewSrv = null;
            previewTexture?.Dispose();
            previewTexture = null;
            previewDevicePointer = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (effect.LastProvider == this)
            {
                effect.LastProvider = null;
            }
            ClearPreviewResources();
            mainTexture?.Dispose();
            mainTexture = null;
            source.Dispose();
        }
    }
}
