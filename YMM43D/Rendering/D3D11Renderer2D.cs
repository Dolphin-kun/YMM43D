using System;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YMM43D.Preview;
using Vortice;

namespace YMM43D.Rendering
{
    /// <summary>
    /// 3D描画（I3DProvider 等）の結果を 2D画像（ID2D1Image）に変換して出力するための共通レンダラークラスです。
    /// 各プラグインの2Dプレビュー・動画出力のボイラープレートコードを共通化します。
    /// </summary>
    public class D3D11Renderer2D : IDisposable
    {
        private readonly D3D11RenderSurface surface = new();
        private ID2D1CommandList? commandList;
        private int lastWidth;
        private int lastHeight;

        /// <summary>
        /// SceneCameraオブジェクトを使用してレンダリングします（タイムラインコンテキストから自動的にView行列を計算します）。
        /// </summary>
        public ID2D1Image Render(
            IGraphicsDevicesAndContext devices, 
            int width, 
            int height, 
            TimelineContext timelineContext,
            SceneCamera camera,
            Vector2 d2dOffset,
            Action<ID3D11Device, ID3D11DeviceContext, Matrix4x4, Matrix4x4> drawAction)
        {
            int timelineFrame = timelineContext.Frame;
            int timelineLength = timelineContext.Length;
            int timelineFps = timelineContext.Fps;
            if (camera.TryGetTimelineContext(out int tf, out int tl, out int tfps))
            {
                timelineFrame = tf;
                timelineLength = tl;
                timelineFps = tfps;
            }

            var view = camera.GetViewMatrix(timelineFrame, timelineLength, timelineFps);
            var proj = SceneCamera.GetProjectionMatrix((float)width / Math.Max(1, height));

            return Render(devices, width, height, view, proj, d2dOffset, drawAction);
        }

        /// <summary>
        /// View行列とProjection行列を直接指定してレンダリングします。
        /// EffectDescription.DrawDescription.CameraなどのMatrix4x4を直接使う場合に使用します。
        /// </summary>
        public ID2D1Image Render(
            IGraphicsDevicesAndContext devices,
            int width,
            int height,
            Matrix4x4 view,
            Matrix4x4 proj,
            Vector2 d2dOffset,
            Action<ID3D11Device, ID3D11DeviceContext, Matrix4x4, Matrix4x4> drawAction)
        {
            var d2dContext = devices.DeviceContext;
            if (width <= 0 || height <= 0)
            {
                lock (devices)
                {
                    commandList?.Dispose();
                    commandList = d2dContext.CreateCommandList();
                    d2dContext.Target = commandList;
                    d2dContext.BeginDraw();
                    d2dContext.Clear(null);
                    d2dContext.EndDraw();
                    d2dContext.Target = null;
                    commandList.Close();
                }
                return commandList;
            }

            SharedGraphics.AcquireIndependentDevice(out var d3d, out var context);
            try
            {
                lock (d3d)
                {
                    if (lastWidth != width || lastHeight != height)
                    {
                        surface.Recreate(devices, width, height);
                        lastWidth = width;
                        lastHeight = height;
                    }

                    if (surface.RenderTargetView == null)
                        throw new Exception("RenderTargetView is null");

                    var oldRTVs = new ID3D11RenderTargetView[1];
                    context.OMGetRenderTargets(1, oldRTVs, out ID3D11DepthStencilView? oldDSV);
                    ID3D11RenderTargetView? oldRTV = oldRTVs[0];

                    try
                    {
                        context.OMSetRenderTargets(surface.RenderTargetView, surface.DepthStencilView);
                        context.ClearRenderTargetView(surface.RenderTargetView, new Color4(0, 0, 0, 0));
                        if (surface.DepthStencilView != null)
                            context.ClearDepthStencilView(surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

                        context.RSSetViewport(new Viewport(0, 0, width, height));

                        drawAction(d3d, context, view, proj);
                    }
                    finally
                    {
                        context.OMSetRenderTargets(oldRTV, oldDSV);
                        oldRTV?.Dispose();
                        oldDSV?.Dispose();
                    }

                    context.Flush();
                }
            }
            finally
            {
                SharedGraphics.ReleaseIndependentDevice();
            }

            lock (devices)
            {
                commandList?.Dispose();
                commandList = d2dContext.CreateCommandList();
                d2dContext.Target = commandList;
                d2dContext.BeginDraw();
                if (surface.Bitmap != null)
                {
                    d2dContext.DrawImage(surface.Bitmap, d2dOffset, null, InterpolationMode.Linear, CompositeMode.SourceOver);
                }
                d2dContext.EndDraw();
                d2dContext.Target = null;
                commandList.Close();
            }

            return commandList;
        }

        public void Dispose()
        {
            surface.Dispose();
            commandList?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
