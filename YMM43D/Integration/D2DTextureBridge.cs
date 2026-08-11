using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Integration
{
    /// <summary>
    /// YMM4 が描いた <see cref="ID2D1Image"/> を、3D描画用デバイス上の
    /// テクスチャに変換して供給します。
    /// </summary>
    /// <remarks>
    /// 3D描画は独立したデバイスで行うため、YMM4 本体のデバイスにある画像を
    /// そのままテクスチャとして使うことはできません。共有リソースを介して
    /// GPU 上でコピーします。
    /// <para>
    /// 生成したテクスチャは鍵ごとにキャッシュし、このクラスが所有します。
    /// 呼び出し側が寿命を気にする必要はありません。以前は経路によって所有権が
    /// 変わり、呼び出し側が破棄すべきかどうかをフラグで受け取っていました。
    /// </para>
    /// </remarks>
    public sealed class D2DTextureBridge : IDisposable
    {
        private readonly Lock gate = new();
        private readonly Dictionary<object, SharedItemTexture> cache = [];

        /// <summary>
        /// 画像を <paramref name="targetDevice"/> 上のテクスチャに焼き込み、その参照を返します。
        /// </summary>
        /// <param name="targetDevice">テクスチャを使う側（3D描画用）のデバイス。</param>
        /// <param name="ymmDevices">画像を保持している YMM4 のデバイス。</param>
        /// <param name="image">変換元の画像。</param>
        /// <param name="key">キャッシュの鍵。通常はアイテムのインスタンス。</param>
        /// <param name="sizeInDips">画像の論理サイズ。</param>
        /// <returns>テクスチャの参照。変換できなかった場合は <c>null</c>。</returns>
        public ID3D11ShaderResourceView? GetTexture(
            ID3D11Device targetDevice,
            IGraphicsDevicesAndContext ymmDevices,
            ID2D1Image image,
            object key,
            out Vector2 sizeInDips)
        {
            sizeInDips = Vector2.One;

            var deviceContext = ymmDevices.DeviceContext;
            if (deviceContext is null || deviceContext.NativePointer == nint.Zero)
                return null;

            var bounds = D2DImageBounds.Get(deviceContext, image);
            sizeInDips = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            var (width, height) = D2DImageBounds.ToPixelSize(bounds);

            lock (gate)
            {
                if (cache.TryGetValue(key, out var texture) && !texture.Matches(width, height, ymmDevices.D3D.Device))
                {
                    texture.Dispose();
                    cache.Remove(key);
                    texture = null;
                }

                texture ??= cache[key] = new SharedItemTexture(targetDevice, ymmDevices, width, height);
                texture.Update(ymmDevices, image, bounds);
                return texture.ShaderResourceView;
            }
        }

        /// <summary>キャッシュしているテクスチャをすべて破棄します。</summary>
        public void Clear()
        {
            lock (gate)
            {
                foreach (var texture in cache.Values)
                    texture.Dispose();
                cache.Clear();
            }
        }

        public void Dispose() => Clear();

        /// <summary>
        /// 3D描画側と YMM4 側の両方から参照できる、1枚分のテクスチャ。
        /// </summary>
        private sealed class SharedItemTexture : IDisposable
        {
            private readonly DisposeCollector disposer = new();
            private readonly int width;
            private readonly int height;
            private readonly nint ymmDevicePointer;
            private readonly ID2D1Bitmap1 targetBitmap;

            public ID3D11ShaderResourceView ShaderResourceView { get; }

            public SharedItemTexture(ID3D11Device targetDevice, IGraphicsDevicesAndContext ymmDevices, int width, int height)
            {
                this.width = width;
                this.height = height;
                ymmDevicePointer = ymmDevices.D3D.Device.NativePointer;

                var texture = Collect(targetDevice.CreateTexture2D(new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    MiscFlags = ResourceOptionFlags.Shared,
                }));

                ShaderResourceView = Collect(targetDevice.CreateShaderResourceView(texture));

                using var dxgiResource = texture.QueryInterface<IDXGIResource>();
                var shared = Collect(ymmDevices.D3D.Device.OpenSharedResource<ID3D11Texture2D>(dxgiResource.SharedHandle));
                using var surface = shared.QueryInterface<IDXGISurface>();
                targetBitmap = Collect(ymmDevices.DeviceContext.CreateBitmapFromDxgiSurface(surface));
            }

            /// <summary>
            /// このテクスチャが指定の条件で再利用できるかを返します。
            /// </summary>
            public bool Matches(int width, int height, ID3D11Device ymmDevice)
                => this.width == width
                && this.height == height
                && ymmDevicePointer == ymmDevice.NativePointer;

            /// <summary>
            /// 最新の画像内容を焼き込みます。
            /// </summary>
            public void Update(IGraphicsDevicesAndContext ymmDevices, ID2D1Image image, in RawRectF bounds)
            {
                var deviceContext = ymmDevices.DeviceContext;

                lock (deviceContext)
                {
                    var previousTarget = deviceContext.Target;
                    var previousTransform = deviceContext.Transform;

                    deviceContext.Target = targetBitmap;
                    deviceContext.BeginDraw();
                    deviceContext.Clear(null);
                    // 描画範囲の左上がテクスチャの原点に来るようにずらす。
                    deviceContext.Transform = Matrix3x2.CreateTranslation(-bounds.Left, -bounds.Top);
                    deviceContext.DrawImage(image);
                    deviceContext.EndDraw();

                    deviceContext.Transform = previousTransform;
                    deviceContext.Target = previousTarget;

                    // 3D側のデバイスが読む前に、この書き込みを完了させる。
                    ymmDevices.D3D.Device.ImmediateContext.Flush();
                }
            }

            private T Collect<T>(T resource) where T : IDisposable
            {
                disposer.Collect(resource);
                return resource;
            }

            public void Dispose() => disposer.Dispose();
        }
    }
}
