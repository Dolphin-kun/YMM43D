using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Integration
{
    /// <summary>
    /// 3D描画用のレンダーターゲットと深度バッファを持ち、その結果を
    /// YMM4 側のデバイスから <see cref="ID2D1Bitmap1"/> として参照できるようにします。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 3D描画は <see cref="GraphicsDevicePool"/> の独立デバイスで行い、YMM4 の描画は
    /// 本体のデバイスで行われます。異なるデバイス間で結果を受け渡すため、
    /// 共有リソースとして作成したテクスチャを本体側で開き直しています。
    /// </para>
    /// <para>
    /// 2つのデバイスは別々の命令列を GPU に流すので、書き込みの途中を読まれないよう
    /// 鍵付きミューテックスで受け渡します。<see cref="BeginWrite"/> と
    /// <see cref="EndWrite"/> で描画を挟んでください。読み出し側の鍵は、YMM4 が
    /// コマンドリストを再生し終える時期が分からないため、次に書き込むまで握り続けます。
    /// </para>
    /// </remarks>
    public sealed class RenderSurface3D : IDisposable
    {
        /// <summary>3D描画側が取得する鍵。</summary>
        private const ulong WriteKey = 0;

        /// <summary>YMM4 側が取得する鍵。</summary>
        private const ulong ReadKey = 1;

        /// <summary>相手が鍵を返すのを待つ上限（ミリ秒）。</summary>
        private const int SyncTimeoutMs = 500;

        /// <summary>
        /// 確保する大きさの刻み（ピクセル）。
        /// </summary>
        /// <remarks>
        /// 要求どおりの大きさで確保すると、ホイールで拡大縮小している間は毎フレーム
        /// 作り直しになります。刻みで切り上げて確保し、要求が収まっている限り使い回します。
        /// </remarks>
        private const int SizeGranularity = 128;

        /// <summary>
        /// 作り直したあと、古い資源を手元に残しておく世代数。
        /// </summary>
        /// <remarks>
        /// YMM4 がいつコマンドリストを再生し終えるか分かりません。作り直した直後に
        /// 前の資源を解放すると、まだ参照されているビットマップを消してしまいます。
        /// </remarks>
        private const int RetiredGenerations = 2;

        private readonly List<DisposeCollector> retired = [];

        private DisposeCollector? disposer;
        private DeviceLease? lease;
        private IDXGIKeyedMutex? writeMutex;
        private IDXGIKeyedMutex? readMutex;
        private nint ymmDeviceKey;
        private bool holdsReadLock;
        private bool isBroken;

        public ID3D11RenderTargetView? RenderTargetView { get; private set; }

        public ID3D11DepthStencilView? DepthStencilView { get; private set; }

        /// <summary>YMM4 のデバイスコンテキストで描画できる、3D描画結果のビットマップ。</summary>
        public ID2D1Bitmap1? Bitmap { get; private set; }

        /// <summary>現在確保しているサイズ。未確保なら (0, 0)。</summary>
        public (int Width, int Height) Size { get; private set; }

        /// <summary>
        /// 指定サイズで確保し直します。同じサイズかつ同じデバイスなら何もしません。
        /// </summary>
        /// <param name="deviceContext">
        /// 共有テクスチャを YMM4 側のビットマップとして開くのに使うコンテキスト。
        /// 本体のものではなく、プラグイン専用のものを渡してください。
        /// </param>
        public void Resize(IGraphicsDevicesAndContext ymmDevices, ID2D1DeviceContext deviceContext, int width, int height)
        {
            var ymmDevice = ymmDevices.D3D.Device;

            // デバイスが作り直された場合、開き直したテクスチャは古いデバイスに
            // 属したままなので、大きさが同じでも作り直す。
            if (!isBroken
                && RenderTargetView is not null
                && ymmDeviceKey == ymmDevice.NativePointer
                && Fits(width, height))
            {
                return;
            }

            ReleaseResources();

            if (width <= 0 || height <= 0)
                return;

            width = Quantize(width);
            height = Quantize(height);

            var device = (lease ??= GraphicsDevicePool.Acquire()).Device;

            disposer = new DisposeCollector();

            var colorDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                // YMM4 側のデバイスから、読み書きを取り違えずに開けるようにする。
                MiscFlags = ResourceOptionFlags.SharedKeyedMutex,
            };

            ID3D11Texture2D renderTarget = Collect(device.CreateTexture2D(colorDesc));
            RenderTargetView = Collect(device.CreateRenderTargetView(renderTarget));
            writeMutex = Collect(renderTarget.QueryInterface<IDXGIKeyedMutex>());

            var depthDesc = colorDesc with
            {
                Format = Format.D24_UNorm_S8_UInt,
                BindFlags = BindFlags.DepthStencil,
                MiscFlags = ResourceOptionFlags.None,
            };

            var depthBuffer = Collect(device.CreateTexture2D(depthDesc));
            DepthStencilView = Collect(device.CreateDepthStencilView(depthBuffer));

            using var dxgiResource = renderTarget.QueryInterface<IDXGIResource>();
            var sharedTexture = Collect(ymmDevice.OpenSharedResource<ID3D11Texture2D>(dxgiResource.SharedHandle));
            readMutex = Collect(sharedTexture.QueryInterface<IDXGIKeyedMutex>());

            using var surface = sharedTexture.QueryInterface<IDXGISurface>();
            Bitmap = Collect(deviceContext.CreateBitmapFromDxgiSurface(surface));

            Size = (width, height);
            ymmDeviceKey = ymmDevice.NativePointer;
            isBroken = false;
        }

        /// <summary>
        /// いま確保してある大きさで、要求された大きさを賄えるかを返します。
        /// </summary>
        /// <remarks>
        /// 大きすぎるまま抱え続けないよう、余りが倍を超えたら作り直します。
        /// </remarks>
        private bool Fits(int width, int height)
            => Size.Width >= width
            && Size.Height >= height
            && Size.Width <= Quantize(width) * 2
            && Size.Height <= Quantize(height) * 2;

        private static int Quantize(int length)
            => (Math.Max(length, 1) + SizeGranularity - 1) / SizeGranularity * SizeGranularity;

        /// <summary>
        /// 3D側がこのテクスチャに描き込む権利を取ります。
        /// </summary>
        /// <returns>取得できなかった場合は <c>false</c>。描画は行わないでください。</returns>
        public bool BeginWrite()
        {
            // YMM4 側が前回の結果を読むために握っている鍵を返す。
            ReleaseReadLock();

            if (writeMutex is null || isBroken)
                return false;

            return TryAcquire(writeMutex, WriteKey);
        }

        /// <summary>
        /// 描き込みを終え、YMM4 側が読める状態にします。
        /// </summary>
        public void EndWrite()
        {
            if (writeMutex is null || isBroken)
                return;

            writeMutex.ReleaseSync(ReadKey);

            // YMM4 がコマンドリストをいつ再生するか分からないため、
            // 次に書き込む直前まで読み出し側の鍵を握り続ける。
            holdsReadLock = readMutex is not null && TryAcquire(readMutex, ReadKey);
        }

        private void ReleaseReadLock()
        {
            if (!holdsReadLock || readMutex is null)
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
                // 待ち時間を超えたか、相手が鍵を持ったまま消えた。鍵の持ち主が
                // 分からなくなるため、次の Resize で作り直す。
                isBroken = true;
                return false;
            }
        }

        private T Collect<T>(T resource) where T : IDisposable
        {
            disposer!.Collect(resource);
            return resource;
        }

        private void ReleaseResources()
        {
            if (!isBroken)
            {
                try { ReleaseReadLock(); } catch { }
            }

            holdsReadLock = false;
            writeMutex = null;
            readMutex = null;
            ymmDeviceKey = nint.Zero;

            // すぐには解放せず、数世代あとまで持ち回す。
            if (disposer is not null)
                retired.Add(disposer);

            disposer = null;

            while (retired.Count > RetiredGenerations)
            {
                retired[0].Dispose();
                retired.RemoveAt(0);
            }

            Bitmap = null;
            DepthStencilView = null;
            RenderTargetView = null;
            Size = default;
        }

        public void Dispose()
        {
            ReleaseResources();

            foreach (var generation in retired)
                generation.Dispose();

            retired.Clear();

            lease?.Dispose();
            lease = null;
        }
    }
}
