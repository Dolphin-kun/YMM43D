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
    /// 呼び出し側が寿命を気にする必要はありません。
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

            // 範囲の問い合わせから焼き込みまでを一続きにする。途中で他のスレッドが
            // 同じコンテキストを使うと、調べた範囲と焼いた内容が食い違うだけでなく、
            // コンテキストの内部状態そのものが壊れる。
            lock (D2DGate.Sync)
            {
                // 描画先を差し替えるので、本体のコンテキストは使えない。
                var deviceContext = privateContext.For(ymmDevices);
                if (deviceContext.NativePointer == nint.Zero)
                    return null;

                bounds = D2DImageBounds.Get(deviceContext, image);
                var (width, height) = D2DImageBounds.ToPixelSize(bounds);

                // 極端に大きな画像は、縮めて焼き込む。テクスチャが覆う範囲は変わらない
                // ので、シェーダー側の UV はそのまま使える。落ちるよりは粗いほうがよい。
                var scale = GetFittingScale(width, height);
                if (scale < 1f)
                {
                    width = Math.Max(1, (int)MathF.Ceiling(width * scale));
                    height = Math.Max(1, (int)MathF.Ceiling(height * scale));
                }

                return GetTextureCore(
                    targetDevice, ymmDevices, deviceContext, image, key, bounds, width, height, scale);
            }
        }

        private const long MaxTexturePixels = 4096L * 4096L;

        private static float GetFittingScale(int width, int height)
        {
            var pixels = (long)width * height;

            return pixels <= MaxTexturePixels
                ? 1f
                : MathF.Sqrt((float)MaxTexturePixels / pixels);
        }

        private ID3D11ShaderResourceView? GetTextureCore(
            ID3D11Device targetDevice,
            IGraphicsDevicesAndContext ymmDevices,
            ID2D1DeviceContext deviceContext,
            ID2D1Image image,
            object key,
            in RawRectF bounds,
            int width,
            int height,
            float scale)
        {
            lock (gate)
            {
                if (cache.TryGetValue(key, out var texture) && !texture.Matches(width, height, ymmDevices.D3D.Device))
                {
                    texture.Dispose();
                    cache.Remove(key);
                    texture = null;
                }

                if (texture is null)
                {
                    try
                    {
                        texture = cache[key] = new SharedItemTexture(
                            targetDevice, ymmDevices, deviceContext, width, height);
                    }
                    catch
                    {
                        // 極端に大きな画像などで確保に失敗することがある。
                        // 落とさず、テクスチャ無しとして扱う。
                        cache.Remove(key);
                        return null;
                    }
                }

                texture.Update(ymmDevices, deviceContext, image, bounds, scale);
                return texture.ShaderResourceView;
            }
        }

        /// <summary>
        /// 指定した鍵に対応しないテクスチャを破棄します。
        /// </summary>
        /// <remarks>
        /// テクスチャは画像1枚ぶんの大きさを占めます。鍵をアイテムにしている呼び出し側は、
        /// タイムライン上から消えたアイテムのぶんをここで手放してください。
        /// </remarks>
        public void RetainOnly(IReadOnlySet<object> keys)
        {
            lock (gate)
            {
                foreach (var key in cache.Keys.Where(k => !keys.Contains(k)).ToArray())
                {
                    if (cache.Remove(key, out var texture))
                        texture.Dispose();
                }
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

        public void Dispose()
        {
            Clear();
            privateContext.Dispose();
        }

        private sealed class SharedItemTexture : IDisposable
        {
            private const ulong WriteKey = 0;

            private const ulong ReadKey = 1;

            private const int SyncTimeoutMs = 500;

            private readonly DisposeCollector disposer = new();
            private readonly int width;
            private readonly int height;
            private readonly nint ymmDevicePointer;
            private readonly ID2D1Bitmap1 targetBitmap;
            private readonly IDXGIKeyedMutex writeMutex;
            private readonly IDXGIKeyedMutex readMutex;

            private bool holdsReadLock;

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

            public bool Matches(int width, int height, ID3D11Device ymmDevice)
                => !isBroken
                && this.width == width
                && this.height == height
                && ymmDevicePointer == ymmDevice.NativePointer;

            public void Update(
                IGraphicsDevicesAndContext ymmDevices,
                ID2D1DeviceContext deviceContext,
                ID2D1Image image,
                in RawRectF bounds,
                float scale)
            {
                // 前フレームの描画で握ったままの鍵を返す。
                ReleaseReadLock();

                if (isBroken || !TryAcquire(writeMutex, WriteKey))
                    return;

                try
                {
                    lock (D2DGate.Sync)
                    {
                        var previousTarget = deviceContext.Target;
                        var previousTransform = deviceContext.Transform;

                        deviceContext.Target = targetBitmap;
                        deviceContext.BeginDraw();
                        deviceContext.Clear(null);
                        // 描画範囲の左上がテクスチャの原点に来るようにずらす。
                        deviceContext.Transform =
                            Matrix3x2.CreateTranslation(-bounds.Left, -bounds.Top)
                            * Matrix3x2.CreateScale(scale);
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
