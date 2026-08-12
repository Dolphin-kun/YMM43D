using System.Numerics;
using Vortice;
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
        private const int CommandListRetention = 3;

        private readonly RenderSurface3D surface = new();
        private readonly PrivateD2DContext privateContext = new();
        private readonly ID2D1CommandList?[] commandLists = new ID2D1CommandList?[CommandListRetention];
        private int commandListIndex;

        /// <summary>何も描かれていない画像を返します。</summary>
        /// <remarks><c>null</c> を返すと、YMM4 が結果を受け取る際に例外になります。</remarks>
        public ID2D1Image RenderEmpty(IGraphicsDevicesAndContext ymmDevices)
            => BuildCommandList(ymmDevices, null, Vector2.Zero);

        /// <summary>
        /// 3Dシーンを描画し、その結果を含むコマンドリストを返します。
        /// </summary>
        /// <param name="offset">結果を配置する、アイテムの画像の中での左上。</param>
        /// <returns>
        /// 描画結果のコマンドリスト。数回 <see cref="Render"/> を呼ぶか
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

            // 入れ子の順序は Direct2D の鍵 → 3D デバイスで固定する。逆順に取る箇所を
            // 1つでも作ると詰まる。
            //
            // コマンドリストの組み立てまで鍵を握り続ける。ここで手放すと、描き終えた
            // 結果を DrawImage に渡すまでの隙に別のスレッドが Resize を走らせ、
            // 破棄済みのビットマップを渡してしまう。
            lock (D2DGate.Sync)
            lock (lease.Device)
            {
                var d2dContext = privateContext.For(ymmDevices);
                surface.Resize(ymmDevices, d2dContext, width, height);

                if (surface.RenderTargetView is null)
                    return BuildCommandList(ymmDevices, null, offset);

                // YMM4 側が前回の結果を読み終えるまで待ってから描き換える。
                if (!surface.BeginWrite())
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

                // 確保してあるビットマップは要求より大きいことがある。実際に描いた
                // 範囲だけを切り出さないと、余白のぶん画像が大きくなってしまう。
                return BuildCommandList(
                    ymmDevices, surface.Bitmap, offset, new RawRectF(0, 0, width, height));
            }
        }

        private ID2D1Image BuildCommandList(
            IGraphicsDevicesAndContext ymmDevices,
            ID2D1Bitmap1? bitmap,
            Vector2 offset,
            RawRectF? sourceRectangle = null)
        {
            lock (D2DGate.Sync)
            {
                // 本体のコンテキストではなく専用のものを使う。描画先や描画中状態を
                // 書き換えるため、共用すると本体側の描画を壊してしまう。
                var deviceContext = privateContext.For(ymmDevices);

                commandListIndex = (commandListIndex + 1) % commandLists.Length;
                commandLists[commandListIndex]?.Dispose();

                var commandList = commandLists[commandListIndex] = deviceContext.CreateCommandList();

                deviceContext.Target = commandList;
                deviceContext.BeginDraw();
                deviceContext.Clear(null);

                if (bitmap is not null)
                    deviceContext.DrawImage(
                        bitmap, offset, sourceRectangle, InterpolationMode.Linear, CompositeMode.SourceOver);

                deviceContext.EndDraw();
                deviceContext.Target = null;

                commandList.Close();
                return commandList;
            }
        }

        public void Dispose()
        {
            surface.Dispose();

            lock (D2DGate.Sync)
            {
                for (var i = 0; i < commandLists.Length; i++)
                {
                    commandLists[i]?.Dispose();
                    commandLists[i] = null;
                }
            }

            privateContext.Dispose();
        }
    }
}
