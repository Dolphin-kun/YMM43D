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
        private readonly PrivateD2DContext privateContext = new();

        /// <summary>
        /// 画像を <paramref name="targetDevice"/> 上のテクスチャに焼き込み、その参照を返します。
        /// </summary>
        /// <param name="targetDevice">テクスチャを使う側（3D描画用）のデバイス。</param>
        /// <param name="ymmDevices">画像を保持している YMM4 のデバイス。</param>
        /// <param name="image">変換元の画像。</param>
        /// <param name="key">キャッシュの鍵。通常はアイテムのインスタンス。</param>
        /// <param name="bounds">
        /// 画像の描画範囲。大きさだけでなく、原点からのずれを知るのにも使えます。
        /// </param>
        /// <returns>テクスチャの参照。変換できなかった場合は <c>null</c>。</returns>
        public ID3D11ShaderResourceView? GetTexture(
            ID3D11Device targetDevice,
            IGraphicsDevicesAndContext ymmDevices,
            ID2D1Image image,
            object key,
            out RawRectF bounds)
        {
            bounds = new RawRectF(0, 0, 1, 1);

            // 描画先を差し替えるので、本体のコンテキストは使えない。
            var deviceContext = privateContext.For(ymmDevices);
            if (deviceContext.NativePointer == nint.Zero)
                return null;

            bounds = D2DImageBounds.Get(deviceContext, image);
            var (width, height) = D2DImageBounds.ToPixelSize(bounds);

            lock (gate)
            {
                if (cache.TryGetValue(key, out var texture) && !texture.Matches(width, height, ymmDevices.D3D.Device))
                {
                    texture.Dispose();
                    cache.Remove(key);
                    texture = null;
                }

                texture ??= cache[key] = new SharedItemTexture(targetDevice, ymmDevices, deviceContext, width, height);
                texture.Update(ymmDevices, deviceContext, image, bounds);
                return texture.ShaderResourceView;
            }
        }

        /// <summary>
        /// 画像の描画範囲だけを調べます。テクスチャは作りません。
        /// </summary>
        /// <remarks>
        /// 実寸だけが必要で、まだ 3D 描画用のデバイスが決まっていない場面で使います。
        /// </remarks>
        public RawRectF GetBounds(IGraphicsDevicesAndContext ymmDevices, ID2D1Image image)
            => D2DImageBounds.Get(privateContext.For(ymmDevices), image);

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

        public void Dispose()
        {
            Clear();
            privateContext.Dispose();
        }

        /// <summary>
        /// 3D描画側と YMM4 側の両方から参照できる、1枚分のテクスチャ。
        /// </summary>
        /// <remarks>
        /// 書き込むのは YMM4 のデバイス、読むのは 3D 描画用のデバイスで、両者は
        /// 別々の命令列を GPU に流します。何も調整しないと、書き込みが終わる前に
        /// 読み出しが走り、消去直後の透明な状態が見えてしまいます。これが
        /// プレビューのちらつきの正体でした。
        /// <para>
        /// そこで鍵付きミューテックスで受け渡しします。書き込み側が鍵 0 を取って
        /// 描き、鍵 1 で手放す。読み出し側は鍵 1 を取り、次に書き込む直前まで
        /// 握り続けてから鍵 0 で返します。GPU 側で順序が保証されるため、
        /// 中途半端な状態を読むことがなくなります。
        /// </para>
        /// </remarks>
        private sealed class SharedItemTexture : IDisposable
        {
            /// <summary>書き込み側（YMM4 のデバイス）が取得する鍵。</summary>
            private const ulong WriteKey = 0;

            /// <summary>読み出し側（3D 描画用のデバイス）が取得する鍵。</summary>
            private const ulong ReadKey = 1;

            /// <summary>相手が鍵を返すのを待つ上限（ミリ秒）。</summary>
            private const int SyncTimeoutMs = 500;

            private readonly DisposeCollector disposer = new();
            private readonly int width;
            private readonly int height;
            private readonly nint ymmDevicePointer;
            private readonly ID2D1Bitmap1 targetBitmap;
            private readonly IDXGIKeyedMutex writeMutex;
            private readonly IDXGIKeyedMutex readMutex;

            private bool holdsReadLock;

            /// <summary>
            /// 鍵の受け渡しに失敗し、このテクスチャが使えなくなったかどうか。
            /// </summary>
            /// <remarks>
            /// 鍵を取り損ねると誰も持っていない鍵番号のまま止まってしまうため、
            /// 復帰させずに作り直します。
            /// </remarks>
            private bool isBroken;

            public ID3D11ShaderResourceView ShaderResourceView { get; }

            public SharedItemTexture(
                ID3D11Device targetDevice,
                IGraphicsDevicesAndContext ymmDevices,
                ID2D1DeviceContext deviceContext,
                int width,
                int height)
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
                    // 両デバイスの読み書きを取り違えないよう、鍵付きで共有する。
                    MiscFlags = ResourceOptionFlags.SharedKeyedMutex,
                }));

                ShaderResourceView = Collect(targetDevice.CreateShaderResourceView(texture));
                readMutex = Collect(texture.QueryInterface<IDXGIKeyedMutex>());

                using var dxgiResource = texture.QueryInterface<IDXGIResource>();
                var shared = Collect(ymmDevices.D3D.Device.OpenSharedResource<ID3D11Texture2D>(dxgiResource.SharedHandle));
                writeMutex = Collect(shared.QueryInterface<IDXGIKeyedMutex>());

                using var surface = shared.QueryInterface<IDXGISurface>();
                targetBitmap = Collect(deviceContext.CreateBitmapFromDxgiSurface(surface));
            }

            /// <summary>
            /// このテクスチャが指定の条件で再利用できるかを返します。
            /// </summary>
            public bool Matches(int width, int height, ID3D11Device ymmDevice)
                => !isBroken
                && this.width == width
                && this.height == height
                && ymmDevicePointer == ymmDevice.NativePointer;

            /// <summary>
            /// 最新の画像内容を焼き込みます。
            /// </summary>
            public void Update(
                IGraphicsDevicesAndContext ymmDevices,
                ID2D1DeviceContext deviceContext,
                ID2D1Image image,
                in RawRectF bounds)
            {
                // 前フレームの描画で握ったままの鍵を返す。
                ReleaseReadLock();

                if (isBroken || !TryAcquire(writeMutex, WriteKey))
                    return;

                try
                {
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

                        // 鍵を手放す前に、溜まっている描画命令を GPU に送り出す。
                        ymmDevices.D3D.Device.ImmediateContext.Flush();
                    }
                }
                finally
                {
                    writeMutex.ReleaseSync(ReadKey);
                }

                // 3D 側が読み終えるまで握り続ける。返すのは次に書き込む直前。
                holdsReadLock = TryAcquire(readMutex, ReadKey);
            }

            private void ReleaseReadLock()
            {
                if (!holdsReadLock)
                    return;

                holdsReadLock = false;
                readMutex.ReleaseSync(WriteKey);
            }

            private bool TryAcquire(IDXGIKeyedMutex mutex, ulong key)
            {
                try
                {
                    mutex.AcquireSync(key, SyncTimeoutMs);
                    return true;
                }
                catch
                {
                    // 待ち時間を超えたか、相手が鍵を持ったまま消えた。どちらの場合も
                    // 鍵の持ち主が分からなくなるため、このテクスチャは作り直す。
                    isBroken = true;
                    return false;
                }
            }

            private T Collect<T>(T resource) where T : IDisposable
            {
                disposer.Collect(resource);
                return resource;
            }

            public void Dispose()
            {
                if (!isBroken)
                {
                    try { ReleaseReadLock(); } catch { }
                }

                disposer.Dispose();
            }
        }
    }
}
