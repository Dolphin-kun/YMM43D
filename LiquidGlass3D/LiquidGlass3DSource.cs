using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace LiquidGlass3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlassConstants
    {
        public Vector4 Size;

        public Vector4 Camera;

        public Vector4 Optics;

        public Vector4 Tint;

        public Vector4 Surface;
    }

    internal sealed class LiquidGlass3DSource(IGraphicsDevicesAndContext devices, LiquidGlass3DParameter parameter)
        : Shape3DSourceBase(devices)
    {
        private const float BoundsMargin = 1.6f;

        private const int MaxCaptureSize = 4096;

        private readonly LiquidGlass3DParameter parameter = parameter;
        private readonly DeviceResourceCache<GlassResources> resources = new(device => new GlassResources(device));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (render.IsShadowPass || render.IsCapturingScene || !resources.TryGet(render.Device, out var shared))
                return;

            var time = item.Time;
            var size = parameter.GetSizePixels(time);

            if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
                return;

            var world = parameter.GetLocalMatrix(time) * item.World;

            if (!Matrix4x4.Invert(world, out var inverse))
                return;

            var captured = item.DepthOnly ? null : shared.Capture(render, this);

            var constants = new GlassConstants
            {
                Size = new Vector4(size, MathF.Max(parameter.CornerRadius.GetFloat(time), 0f)),
                Camera = new Vector4(
                    Vector3.Transform(render.GetCameraPosition(), inverse),
                    parameter.IsSphere ? 1f : 0f),
                Optics = new Vector4(
                    Math.Clamp(parameter.RefractiveIndex.GetFloat(time), 1f, 4f),
                    MathF.Max(parameter.Distance.GetFloat(time), 0f),
                    Math.Clamp(parameter.Frost.GetFloat(time) / 100f, 0f, 1f),
                    MathF.Max(parameter.Dispersion.GetFloat(time), 0f) / 100f),
                Tint = new Vector4(parameter.TintColor.ToVector3(), Math.Clamp(parameter.TintAmount.GetFloat(time) / 100f, 0f, 1f)),
                Surface = new Vector4(
                    Math.Clamp(parameter.Reflection.GetFloat(time) / 100f, 0f, 1f),
                    captured is { } capture ? capture.MipLevels : 0f,
                    captured is null ? 0f : 1f,
                    0f),
            };

            var gloss = SurfaceGloss.FromPercent(parameter.Gloss.GetFloat(time), parameter.GlossSharpness.GetFloat(time));

            shared.Pipeline.Draw(
                render.Context,
                render.CreateConstants(world, item.Opacity, alphaCutoff: 0f, gloss: gloss),
                constants,
                item.ToDrawSettings(FaceCulling.Front, captured?.View) with
                {
                    Sampler = RenderStates.For(render.Device).LinearSampler,
                },
                shared.Mesh);
        }

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            var corners = new Vector3[8];
            var half = BoundsMargin * 0.5f;

            for (var i = 0; i < corners.Length; i++)
                corners[i] = new Vector3((i & 1) == 0 ? -half : half, (i & 2) == 0 ? -half : half, (i & 4) == 0 ? -half : half);

            return WorldBounds.FromPoints(corners, parameter.GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed record CaptureTarget(ID3D11ShaderResourceView View, int MipLevels);

        private sealed class GlassResources(ID3D11Device device) : IDisposable
        {
            private ID3D11Texture2D? color;
            private ID3D11RenderTargetView? colorTarget;
            private ID3D11ShaderResourceView? colorView;
            private ID3D11Texture2D? depth;
            private ID3D11DepthStencilView? depthTarget;
            private int width;
            private int height;
            private int mipLevels;
            private (object? Scene, Matrix4x4 View, Matrix4x4 Projection) captured;

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device,
                Vertex.InputElements,
                new ShaderMaterial(device, typeof(GlassResources).Assembly, "LiquidGlass3D.hlsl"));

            public BoxMesh Mesh { get; } = BoxMesh.CreateUnitCube(device);

            public CaptureTarget? Capture(in Render3DContext render, object self)
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
                    && captured.Projection == render.Projection;

                if (reusable)
                    return new CaptureTarget(colorView!, mipLevels);

                Ensure(captureWidth, captureHeight);

                if (colorTarget is null || depthTarget is null || colorView is null)
                    return null;

                captured = (render.Scene, render.View, render.Projection);

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
                        if (ReferenceEquals(occluder.Provider, self))
                            continue;

                        if (occluder.Provider is I3DBounds bounded
                            && render.IsOutside(bounded.GetLocalBounds(occluder.Time), occluder.World))
                        {
                            continue;
                        }

                        try
                        {
                            occluder.Provider.Draw(capturing, new DrawContext3D
                            {
                                World = occluder.World,
                                Opacity = 1f,
                                Time = occluder.Time,
                            });
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

                ReleaseTargets();

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

            private void ReleaseTargets()
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

            public void Dispose()
            {
                ReleaseTargets();
                Mesh.Dispose();
                Pipeline.Dispose();
            }
        }
    }
}
