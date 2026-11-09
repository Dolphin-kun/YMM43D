using System.Numerics;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Player
{
    public sealed class Renderer3DTo2D : IDisposable
    {
        private const int CommandListRetention = 3;

        private readonly RenderSurface3D surface = new();
        private readonly PrivateD2DContext privateContext = new();
        private readonly ID2D1CommandList?[] commandLists = new ID2D1CommandList?[CommandListRetention];
        private int commandListIndex;

        private readonly ID3D11RenderTargetView?[] previousTargets = new ID3D11RenderTargetView?[1];
        private Viewport[] previousViewports = new Viewport[8];

        public ID2D1Image RenderEmpty(IGraphicsDevicesAndContext ymmDevices)
            => BuildEmpty(ymmDevices, Vector2.Zero);

        public ID2D1Image Render(
            IGraphicsDevicesAndContext ymmDevices,
            int width,
            int height,
            Matrix4x4 view,
            Matrix4x4 projection,
            Vector2 offset,
            SceneLighting? lighting,
            IReadOnlyList<SceneDepthCollector.Occluder> shadowCasters,
            Color4 background,
            Action<Render3DContext> draw,
            float density = 1f)
        {
            if (width <= 0 || height <= 0)
                return BuildEmpty(ymmDevices, offset);

            var dense = float.IsFinite(density) ? MathF.Max(density, 1f) : 1f;
            var pixelWidth = Math.Max(1, (int)MathF.Ceiling(width * dense));
            var pixelHeight = Math.Max(1, (int)MathF.Ceiling(height * dense));

            using var lease = GraphicsDevicePool.Acquire();
            var context = lease.Context;

            lock (D2DGate.Sync)
            lock (lease.Device)
            {
                if (GraphicsDevicePool.IsDeviceLost(out _))
                    return BuildEmpty(ymmDevices, offset);

                var d2dContext = privateContext.For(ymmDevices);
                surface.Resize(ymmDevices, d2dContext, pixelWidth, pixelHeight);

                if (surface.RenderTargetView is null)
                    return BuildEmpty(ymmDevices, offset);

                if (!surface.BeginWrite())
                    return BuildEmpty(ymmDevices, offset);

                context.OMGetRenderTargets(1, previousTargets!, out var previousDepth);
                var previousTarget = previousTargets[0];
                previousTargets[0] = null;

                var viewportCount = context.RSGetViewports();

                if (viewportCount > previousViewports.Length)
                    previousViewports = new Viewport[viewportCount];

                if (viewportCount > 0)
                    context.RSGetViewports(previousViewports.AsSpan(0, viewportCount));

                var lost = false;

                try
                {
                    var lit = SceneShadows.Build(
                        lease.Device, context, lighting ?? SceneLighting.Default, shadowCasters, this, ymmDevices);

                    context.OMSetRenderTargets(surface.RenderTargetView, surface.DepthStencilView);
                    context.ClearRenderTargetView(surface.RenderTargetView, background);
                    if (surface.DepthStencilView is not null)
                        context.ClearDepthStencilView(surface.DepthStencilView, DepthStencilClearFlags.Depth, 1f, 0);

                    context.RSSetViewport(new Viewport(0, 0, pixelWidth, pixelHeight));

                    draw(new Render3DContext(lease.Device, context, view, projection, lit)
                    {
                        Scene = shadowCasters,
                        SourceDevices = ymmDevices,
                        IsOutput = true,
                    }.BindLights());

                    context.Flush();
                }
                catch (SharpGenException) when (GraphicsDevicePool.IsDeviceLost(out _))
                {
                    lost = true;
                }
                finally
                {
                    context.OMSetRenderTargets(previousTarget!, previousDepth);
                    previousTarget?.Dispose();
                    previousDepth?.Dispose();

                    if (viewportCount > 0)
                        context.RSSetViewports(previousViewports.AsSpan(0, viewportCount));

                    surface.EndWrite();
                }

                if (lost || surface.Bitmap is not { } bitmap)
                    return BuildEmpty(ymmDevices, offset);

                return BuildPacked(
                    ymmDevices, bitmap, offset, new Vector2(width, height), pixelWidth, pixelHeight);
            }
        }

        private ID2D1CommandList BuildEmpty(IGraphicsDevicesAndContext ymmDevices, Vector2 offset)
        {
            lock (D2DGate.Sync)
            {
                var deviceContext = OpenList(ymmDevices, out var commandList);

                using var clear = deviceContext.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 0f));

                deviceContext.FillRectangle(new RawRectF(offset.X, offset.Y, offset.X + 1f, offset.Y + 1f), clear);

                return CloseList(deviceContext, commandList);
            }
        }

        private ID2D1CommandList BuildPacked(
            IGraphicsDevicesAndContext ymmDevices,
            ID2D1Bitmap1 bitmap,
            Vector2 offset,
            Vector2 span,
            int pixelWidth,
            int pixelHeight)
        {
            lock (D2DGate.Sync)
            {
                var deviceContext = OpenList(ymmDevices, out var commandList);

                deviceContext.DrawBitmap(
                    bitmap,
                    new RawRectF(offset.X, offset.Y, offset.X + span.X, offset.Y + span.Y),
                    1f,
                    InterpolationMode.Linear,
                    new RawRectF(0, 0, pixelWidth, pixelHeight),
                    null);

                return CloseList(deviceContext, commandList);
            }
        }

        private ID2D1DeviceContext OpenList(
            IGraphicsDevicesAndContext ymmDevices, out ID2D1CommandList commandList)
        {
            var deviceContext = privateContext.For(ymmDevices);

            commandListIndex = (commandListIndex + 1) % commandLists.Length;
            commandLists[commandListIndex]?.Dispose();

            commandList = commandLists[commandListIndex] = deviceContext.CreateCommandList();

            deviceContext.Target = commandList;
            deviceContext.BeginDraw();
            deviceContext.Clear(null);

            return deviceContext;
        }

        private static ID2D1CommandList CloseList(
            ID2D1DeviceContext deviceContext, ID2D1CommandList commandList)
        {
            deviceContext.EndDraw();
            deviceContext.Target = null;

            commandList.Close();

            return commandList;
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
