using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Commons;

namespace LiquidGlass3D
{
    internal sealed record CaptureTarget(ID3D11ShaderResourceView View, int MipLevels);

    internal sealed class SceneCapture(ID3D11Device device) : IDisposable
    {
        private const int MaxCaptureSize = 4096;
        private const float DepthMargin = 1e-3f;

        private ID3D11Texture2D? color;
        private ID3D11RenderTargetView? colorTarget;
        private ID3D11ShaderResourceView? colorView;
        private ID3D11Texture2D? depth;
        private ID3D11DepthStencilView? depthTarget;
        private int width;
        private int height;
        private int mipLevels;
        private (object? Scene, Matrix4x4 View, Matrix4x4 Projection, float Front) captured;

        public static float NearDepth(in WorldBounds bounds, in Matrix4x4 world, in Matrix4x4 view)
            => Depths(bounds, world, view, out var near, out _) ? near : float.MinValue;

        public static float FarDepth(in WorldBounds bounds, in Matrix4x4 world, in Matrix4x4 view)
            => Depths(bounds, world, view, out _, out var far) ? far : float.MaxValue;

        private static bool Depths(in WorldBounds bounds, in Matrix4x4 world, in Matrix4x4 view, out float near, out float far)
        {
            near = float.MaxValue;
            far = float.MinValue;

            if (bounds.IsEmpty)
                return false;

            Span<Vector3> corners = stackalloc Vector3[WorldBounds.CornerCount];
            bounds.WriteCorners(corners);

            foreach (var corner in corners)
            {
                var distance = -Vector3.Transform(Vector3.Transform(corner, world), view).Z;

                if (!float.IsFinite(distance))
                    return false;

                near = MathF.Min(near, distance);
                far = MathF.Max(far, distance);
            }

            return true;
        }

        public CaptureTarget? Capture(in Render3DContext render, IReadOnlyCollection<object?> selves, float front)
        {
            if (render.Scene.Count == 0)
                return null;

            var context = render.Context;
            var viewportCount = context.RSGetViewports();

            if (viewportCount == 0)
                return null;

            var viewports = new Viewport[viewportCount];
            context.RSGetViewports(viewports);

            var captureWidth = Math.Clamp((int)MathF.Ceiling(viewports[0].Width), 1, MaxCaptureSize);
            var captureHeight = Math.Clamp((int)MathF.Ceiling(viewports[0].Height), 1, MaxCaptureSize);

            var reusable = colorView is not null
                && width == captureWidth
                && height == captureHeight
                && ReferenceEquals(captured.Scene, render.Scene)
                && captured.View == render.View
                && captured.Projection == render.Projection
                && MathF.Abs(captured.Front - front) < 1e-4f;

            if (reusable)
                return new CaptureTarget(colorView!, mipLevels);

            Ensure(captureWidth, captureHeight);

            if (colorTarget is null || depthTarget is null || colorView is null)
                return null;

            captured = (render.Scene, render.View, render.Projection, front);

            var previousTargets = new ID3D11RenderTargetView[1];
            context.OMGetRenderTargets(1, previousTargets, out var previousDepth);

            try
            {
                context.OMSetRenderTargets(colorTarget, depthTarget);
                context.ClearRenderTargetView(colorTarget, new Color4(0f, 0f, 0f, 0f));
                context.ClearDepthStencilView(depthTarget, DepthStencilClearFlags.Depth, 1f, 0);
                context.RSSetViewport(new Viewport(0, 0, captureWidth, captureHeight));

                var capturing = render with { IsCapturingScene = true };

                foreach (var occluder in render.Scene)
                {
                    if (selves.Any(self => ReferenceEquals(occluder.Provider, self)))
                        continue;

                    if (occluder.Provider is I3DBounds bounded)
                    {
                        var bounds = bounded.GetLocalBounds(occluder.Time);

                        if (render.IsOutside(bounds, occluder.World))
                            continue;

                        if (FarDepth(bounds, occluder.World, render.View) < front - DepthMargin)
                            continue;
                    }

                    try
                    {
                        occluder.Provider.Draw(capturing, new DrawContext3D { World = occluder.World, Opacity = 1f, Time = occluder.Time });
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                context.OMSetRenderTargets(previousTargets[0], previousDepth);
                previousTargets[0]?.Dispose();
                previousDepth?.Dispose();
                context.RSSetViewports(viewports);
            }

            context.GenerateMips(colorView);

            return new CaptureTarget(colorView, mipLevels);
        }

        private void Ensure(int newWidth, int newHeight)
        {
            if (colorView is not null && width == newWidth && height == newHeight)
                return;

            Release();

            width = newWidth;
            height = newHeight;
            mipLevels = (int)MathF.Floor(MathF.Log2(Math.Max(width, height))) + 1;

            color = device.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = mipLevels,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.GenerateMips,
            });
            colorTarget = device.CreateRenderTargetView(color);
            colorView = device.CreateShaderResourceView(color);

            depth = device.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.D24_UNorm_S8_UInt,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
            });
            depthTarget = device.CreateDepthStencilView(depth);
        }

        private void Release()
        {
            captured = default;

            colorView?.Dispose();
            colorTarget?.Dispose();
            color?.Dispose();
            depthTarget?.Dispose();
            depth?.Dispose();

            colorView = null;
            colorTarget = null;
            color = null;
            depthTarget = null;
            depth = null;
        }

        public void Dispose() => Release();
    }
}
