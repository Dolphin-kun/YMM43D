using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YukkuriMovieMaker.Commons;

namespace FrameImpact3D
{
    internal sealed class ImpactFramePass : IDisposable
    {
        private const int CanvasSlot = 13;
        private const int CanvasDepthSlot = 14;

        private readonly DisposeCollector disposer = new();
        private readonly ID3D11Device device;
        private readonly RenderPipeline<TransformConstants> pipeline;
        private readonly ImpactMesh screen;

        private ImpactCanvas? canvas;

        public ImpactFramePass(ID3D11Device device)
        {
            this.device = device;

            pipeline = disposer.CollectEx(new RenderPipeline<TransformConstants>(device, ImpactVertex.InputElements, new ShaderMaterial(device, typeof(ImpactFramePass).Assembly, "ImpactFrame.hlsl")));
            screen = disposer.CollectEx(new ImpactMesh(device, new ImpactPattern(ImpactKind.Flash, 1, 0)));
        }

        public ImpactFrameTarget? Begin(ID3D11DeviceContext context)
        {
            var targets = new ID3D11RenderTargetView[1];

            context.OMGetRenderTargets(1, targets, out var depth);

            if (targets[0] is not { } target)
            {
                depth?.Dispose();
                return null;
            }

            using var resource = target.Resource;
            using var texture = resource.QueryInterface<ID3D11Texture2D>();

            var description = texture.Description;

            if (description.SampleDescription.Count != 1)
            {
                target.Dispose();
                depth?.Dispose();
                return null;
            }

            var surface = Reserve(description.Width, description.Height, target.Description.Format);

            context.ClearRenderTargetView(surface.Target, new Color4(0f, 0f, 0f, 0f));
            context.ClearDepthStencilView(surface.Depth, DepthStencilClearFlags.Depth, 1f, 0);
            context.OMSetRenderTargets(surface.Target, surface.Depth);

            return new ImpactFrameTarget(target, depth, new Vector2(description.Width, description.Height));
        }

        public void End(in Render3DContext render, ImpactFrameTarget target, in ImpactFrameLook look, Vector3 focus, bool isAlwaysOnTop)
        {
            var context = render.Context;

            context.OMSetRenderTargets(target.Target, target.Depth);

            try
            {
                if (canvas is not { } surface)
                    return;

                var (center, pixels) = Measure(context, render, focus);
                var heading = look.Aim.Focus > 0f ? Heading(context, render, focus, look.Aim.Direction, center) : Vector2.Zero;
                var area = look.Area.IsWhole ? Vector4.Zero : new Vector4(MathF.Max(look.Area.Radius * pixels, 1f), MathF.Max(look.Area.Softness * pixels, 1f), 0f, 0f);

                var constants = new ImpactFrameConstants
                {
                    Look = new Vector4((float)look.Kind, look.Threshold, look.Edge * pixels, 0f),
                    Spread = new Vector4(look.Blur * pixels, look.Shift * pixels, look.Glow, 0f),
                    Focus = new Vector4(center, target.Size.X, target.Size.Y),
                    Tint = new Vector4(look.Tint, 1f),
                    Range = area,
                    Aim = new Vector4(heading, heading == Vector2.Zero ? 0f : 1f, 0f),
                };

                var settings = new DrawSettings { Blend = BlendMode.Normal, Culling = FaceCulling.None, IgnoreDepth = isAlwaysOnTop || target.Depth is null };

                context.PSSetShaderResource(CanvasSlot, surface.View);
                context.PSSetShaderResource(CanvasDepthSlot, surface.DepthView);
                pipeline.Draw(context, render.CreateConstants(Matrix4x4.Identity, 1f, unlit: true), constants, settings, screen);
            }
            finally
            {
                context.PSSetShaderResource(CanvasSlot, null!);
                context.PSSetShaderResource(CanvasDepthSlot, null!);

                target.Target.Dispose();
                target.Depth?.Dispose();
            }
        }

        private static (Vector2 Center, float Pixels) Measure(ID3D11DeviceContext context, in Render3DContext render, Vector3 focus)
        {
            var viewport = CurrentViewport(context);
            var clip = Vector4.Transform(new Vector4(focus, 1f), render.ViewProjection);
            var depth = MathF.Max(clip.W, 1e-4f);
            var pixels = WorldScale.ToWorld(1f) * viewport.Height * render.Projection.M22 / (2f * depth);

            return (ToPixel(viewport, clip), pixels);
        }

        private static Vector2 Heading(ID3D11DeviceContext context, in Render3DContext render, Vector3 focus, Vector3 direction, Vector2 center)
        {
            var clip = Vector4.Transform(new Vector4(focus + direction, 1f), render.ViewProjection);

            if (clip.W <= 1e-4f)
                return Vector2.Zero;

            var shift = ToPixel(CurrentViewport(context), clip) - center;

            return shift.LengthSquared() > 1e-8f ? Vector2.Normalize(shift) : Vector2.Zero;
        }

        private static Vector2 ToPixel(in Viewport viewport, Vector4 clip)
        {
            var depth = MathF.Max(clip.W, 1e-4f);
            var ndc = new Vector2(clip.X, clip.Y) / depth;

            return new Vector2(viewport.X + (ndc.X * 0.5f + 0.5f) * viewport.Width, viewport.Y + (0.5f - ndc.Y * 0.5f) * viewport.Height);
        }

        private static Viewport CurrentViewport(ID3D11DeviceContext context)
        {
            var viewports = new Viewport[1];

            if (context.RSGetViewports() > 0)
                context.RSGetViewports(viewports);

            return viewports[0];
        }

        private ImpactCanvas Reserve(int width, int height, Format format)
        {
            if (canvas is { } held && held.Width == width && held.Height == height && held.Format == format)
                return held;

            canvas?.Dispose();

            return canvas = new ImpactCanvas(device, width, height, format);
        }

        public void Dispose()
        {
            canvas?.Dispose();
            canvas = null;
            disposer.Dispose();
        }
    }
}
