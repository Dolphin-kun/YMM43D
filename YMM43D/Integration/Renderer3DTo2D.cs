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
        private ID2D1CommandList? commandList;

        /// <summary>
        /// 何も描かれていない画像を返します。
        /// </summary>
        /// <remarks>
        /// 描画するものが無い場合に使います。<c>null</c> を返してしまうと、
        /// YMM4 が結果を受け取る際に例外になります。
        /// </remarks>
        public ID2D1Image RenderEmpty(IGraphicsDevicesAndContext ymmDevices)
            => BuildCommandList(ymmDevices, null, Vector2.Zero);

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
            Action<Render3DContext> draw)
        {
            if (width <= 0 || height <= 0)
                return BuildCommandList(ymmDevices, null, offset);

            using var lease = GraphicsDevicePool.Acquire();
            var context = lease.Context;

            lock (lease.Device)
            {
                surface.Resize(ymmDevices, width, height);
                if (surface.RenderTargetView is null)
                    return BuildCommandList(ymmDevices, null, offset);

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
                }
                finally
                {
                    context.OMSetRenderTargets(previousTarget, previousDepth);
                    previousTarget?.Dispose();
                    previousDepth?.Dispose();

                    if (previousViewports is not null)
                        context.RSSetViewports(previousViewports);
                }

                // 本体デバイスが共有テクスチャを読む前に、書き込みを完了させる。
                context.Flush();
            }

            return BuildCommandList(ymmDevices, surface.Bitmap, offset);
        }

        private ID2D1Image BuildCommandList(IGraphicsDevicesAndContext ymmDevices, ID2D1Bitmap1? bitmap, Vector2 offset)
        {
            var deviceContext = ymmDevices.DeviceContext;

            lock (ymmDevices)
            {
                commandList?.Dispose();
                commandList = deviceContext.CreateCommandList();

                deviceContext.Target = commandList;
                deviceContext.BeginDraw();
                deviceContext.Clear(null);
                if (bitmap is not null)
                    deviceContext.DrawImage(bitmap, offset, null, InterpolationMode.Linear, CompositeMode.SourceOver);
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
        }
    }
}
