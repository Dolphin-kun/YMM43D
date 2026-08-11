using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Integration
{
    /// <summary>
    /// 3D描画の結果を YMM4 が扱える <see cref="ID2D1Image"/> に変換します。
    /// </summary>
    /// <remarks>
    /// 図形アイテムの <c>IShapeSource.Update</c> や映像エフェクトの出力で必要になる
    /// 「独立デバイスで 3D を描く → 共有テクスチャ経由で本体デバイスに渡す →
    /// コマンドリストにまとめる」という一連の流れを受け持ちます。
    /// </remarks>
    public sealed class Renderer3DTo2D : IDisposable
    {
        private readonly RenderSurface3D surface = new();
        private readonly PrivateD2DContext privateContext = new();
        private ID2D1CommandList? commandList;

        /// <summary>
        /// 何も描かれていない画像を返します。
        /// </summary>
        /// <remarks>
        /// 描画するものが無い場合に使います。<c>null</c> を返してしまうと、
        /// YMM4 が結果を受け取る際に例外になります。
        /// </remarks>
        public ID2D1Image RenderEmpty(IGraphicsDevicesAndContext ymmDevices)
            => BuildCommandList(ymmDevices, null, Vector2.Zero, Matrix3x2.Identity);

        /// <summary>
        /// 3Dシーンを描画し、その結果を含むコマンドリストを返します。
        /// </summary>
        /// <param name="ymmDevices">YMM4 のグラフィックスデバイス。</param>
        /// <param name="width">描画する幅（ピクセル）。</param>
        /// <param name="height">描画する高さ（ピクセル）。</param>
        /// <param name="view">ビュー行列。</param>
        /// <param name="projection">射影行列。</param>
        /// <param name="offset">結果を配置する 2D 上のずれ。</param>
        /// <param name="draw">実際の 3D 描画を行うコールバック。</param>
        /// <param name="imageTransform">
        /// 結果に掛ける 2D 変換。YMM4 がこの画像に後から掛ける拡大・回転を
        /// 打ち消すのに使います。
        /// </param>
        /// <returns>
        /// 描画結果のコマンドリスト。次に <see cref="Render"/> を呼ぶか
        /// このオブジェクトを破棄するまで有効です。
        /// </returns>
        public ID2D1Image Render(
            IGraphicsDevicesAndContext ymmDevices,
            int width,
            int height,
            Matrix4x4 view,
            Matrix4x4 projection,
            Vector2 offset,
            Action<Render3DContext> draw,
            Matrix3x2 imageTransform = default)
        {
            if (imageTransform == default)
                imageTransform = Matrix3x2.Identity;

            if (width <= 0 || height <= 0)
                return BuildCommandList(ymmDevices, null, offset, imageTransform);

            using var lease = GraphicsDevicePool.Acquire();
            var context = lease.Context;

            // 入れ子の順序は 3D デバイス → Direct2D の鍵で固定する。逆順に取る箇所を
            // 作ると詰まるので、この鍵を持ったままデバイスをロックしないこと。
            //
            // コマンドリストの組み立てまで鍵を握り続ける。ここで手放すと、描き終えた
            // 結果を DrawImage に渡すまでの隙に別のスレッドが Resize を走らせ、
            // 破棄済みのビットマップを渡してしまう。
            lock (lease.Device)
            lock (D2DGate.Sync)
            {
                var d2dContext = privateContext.For(ymmDevices);
                surface.Resize(ymmDevices, d2dContext, width, height);

                if (surface.RenderTargetView is null)
                    return BuildCommandList(ymmDevices, null, offset, imageTransform);

                // YMM4 側が前回の結果を読み終えるまで待ってから描き換える。
                if (!surface.BeginWrite())
                    return BuildCommandList(ymmDevices, null, offset, imageTransform);

                // このコンテキストは他の描画とも共有されるため、書き換える状態は
                // すべて退避して必ず戻す。3Dプレビューは描画の途中でこのメソッドを
                // 呼ぶことがあり、戻し漏れがあるとプレビュー側の描画が崩れる。
                var previousTargets = new ID3D11RenderTargetView[1];
                context.OMGetRenderTargets(1, previousTargets, out var previousDepth);
                var previousTarget = previousTargets[0];

                var viewportCount = context.RSGetViewports();
                var previousViewports = viewportCount > 0 ? new Viewport[viewportCount] : null;
                if (previousViewports is not null)
                    context.RSGetViewports(previousViewports);

                try
                {
                    context.OMSetRenderTargets(surface.RenderTargetView, surface.DepthStencilView);
                    context.ClearRenderTargetView(surface.RenderTargetView, new Color4(0, 0, 0, 0));
                    if (surface.DepthStencilView is not null)
                        context.ClearDepthStencilView(surface.DepthStencilView, DepthStencilClearFlags.Depth, 1f, 0);

                    context.RSSetViewport(new Viewport(0, 0, width, height));

                    draw(new Render3DContext(lease.Device, context, view, projection));

                    // 鍵を手放す前に、溜まっている描画命令を GPU に送り出す。
                    context.Flush();
                }
                finally
                {
                    context.OMSetRenderTargets(previousTarget, previousDepth);
                    previousTarget?.Dispose();
                    previousDepth?.Dispose();

                    if (previousViewports is not null)
                        context.RSSetViewports(previousViewports);

                    surface.EndWrite();
                }

                return BuildCommandList(ymmDevices, surface.Bitmap, offset, imageTransform);
            }
        }

        private ID2D1Image BuildCommandList(
            IGraphicsDevicesAndContext ymmDevices,
            ID2D1Bitmap1? bitmap,
            Vector2 offset,
            Matrix3x2 imageTransform)
        {
            lock (D2DGate.Sync)
            {
                // 本体のコンテキストではなく専用のものを使う。描画先や描画中状態を
                // 書き換えるため、共用すると本体側の描画を壊してしまう。
                var deviceContext = privateContext.For(ymmDevices);

                commandList?.Dispose();
                commandList = deviceContext.CreateCommandList();

                deviceContext.Target = commandList;
                deviceContext.BeginDraw();
                deviceContext.Clear(null);

                // 拡大や回転を打ち消す変換。位置のずれは DrawImage 側で与えるので、
                // ここでは掛からないように変換を設定してから描く順序にしている。
                deviceContext.Transform = imageTransform;

                if (bitmap is not null)
                    deviceContext.DrawImage(bitmap, offset, null, InterpolationMode.Linear, CompositeMode.SourceOver);

                deviceContext.Transform = Matrix3x2.Identity;
                deviceContext.EndDraw();
                deviceContext.Target = null;

                commandList.Close();
                return commandList;
            }
        }

        public void Dispose()
        {
            surface.Dispose();
            commandList?.Dispose();
            commandList = null;
            privateContext.Dispose();
        }
    }
}
