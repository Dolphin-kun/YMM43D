using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace Outline3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct OutlineConstants
    {
        public Vector4 Region;

        public Vector4 Target;

        public Vector4 Inverse;

        public Vector4 Placement;

        public Vector4 Stroke;

        public Vector4 Shape;

        public Vector4 StrokeColor;

        public Vector4 BrushArea;

        public Vector4 DepthMap;
    }

    internal readonly record struct OutlineLook(
        float Thickness,
        float Blur,
        float Sides,
        float Smoothness,
        bool IsOutlineOnly,
        bool IsAngular,
        Vector2 Offset,
        float Zoom,
        float Rotation,
        Vector4 Color,
        ID3D11ShaderResourceView? Brush,
        Vector2 BrushSize);

    internal delegate void SourceDraw(in Render3DContext render, DrawContext3D item);

    internal sealed class OutlinePass : IDisposable
    {
        private const int MaxTargetSize = 16384;

        private const float MinimumZoom = 1e-3f;

        private readonly DisposeCollector disposer = new();
        private readonly ID3D11Device device;
        private readonly ID3D11VertexShader vertexShader;
        private readonly ID3D11PixelShader seedShader;
        private readonly ID3D11PixelShader jumpShader;
        private readonly ID3D11PixelShader compositeShader;
        private readonly ID3D11Buffer constantBuffer;

        private Targets? targets;

        public OutlinePass(ID3D11Device device)
        {
            this.device = device;

            var assembly = typeof(OutlinePass).Assembly;

            vertexShader = Collect(device.CreateVertexShader(
                ShaderLibrary.Compile(assembly, "Outline3D.hlsl", "VSMain", "vs_5_0")));
            seedShader = Collect(device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, "Outline3D.hlsl", "SeedPS", "ps_5_0")));
            jumpShader = Collect(device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, "Outline3D.hlsl", "JumpPS", "ps_5_0")));
            compositeShader = Collect(device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, "Outline3D.hlsl", "CompositePS", "ps_5_0")));
            constantBuffer = Collect(D3D11Buffers.CreateConstantBuffer<OutlineConstants>(device));
        }

        public void Draw(in Render3DContext render, DrawContext3D item, WorldBounds bounds, in OutlineLook look, SourceDraw drawSource)
        {
            var context = render.Context;
            var viewportCount = context.RSGetViewports();

            if (viewportCount == 0 || look.Zoom < MinimumZoom || look.Color.W <= 0f)
                return;

            var viewports = new Viewport[viewportCount];
            context.RSGetViewports(viewports);

            var viewport = viewports[0];
            var width = Math.Clamp((int)MathF.Ceiling(viewport.Width), 1, MaxTargetSize);
            var height = Math.Clamp((int)MathF.Ceiling(viewport.Height), 1, MaxTargetSize);

            if (!TryMeasure(render, item.World, bounds, width, height, out var center, out var pixelsPerItemPixel, out var corners))
                return;

            var thickness = look.Thickness * pixelsPerItemPixel;
            var blur = look.Blur * pixelsPerItemPixel / 2f;

            if (thickness <= 0f && blur <= 0f)
                return;

            var offset = look.Offset * pixelsPerItemPixel;
            var reach = thickness + blur * 4f + 2f;

            var region = MeasureRegion(corners, center, offset, look, reach, width, height);

            if (region.Z <= region.X || region.W <= region.Y)
                return;

            targets = targets is { } current && current.Width == width && current.Height == height
                ? current
                : Recreate(width, height);

            var angle = float.DegreesToRadians(look.Rotation);
            var sides = Math.Clamp(MathF.Round(look.Sides), 3f, 256f);
            var projection = render.Projection;

            var constants = new OutlineConstants
            {
                Region = region,
                Target = new Vector4(0f, 0f, width, height),
                Inverse = new Vector4(MathF.Cos(angle), MathF.Sin(angle), -MathF.Sin(angle), MathF.Cos(angle)) / look.Zoom,
                Placement = new Vector4(center, offset.X, offset.Y),
                Stroke = new Vector4(thickness, blur, MathF.Max(look.Smoothness / 100f * 1.5f, 1e-3f), 1f),
                Shape = new Vector4(look.IsAngular ? 1f : 0f, sides, MathF.Cos(MathF.PI / sides), look.IsOutlineOnly ? 1f : 0f),
                StrokeColor = look.Color,
                BrushArea = new Vector4(
                    pixelsPerItemPixel,
                    MathF.Max(look.BrushSize.X, 1f),
                    MathF.Max(look.BrushSize.Y, 1f),
                    look.Brush is null ? 0f : 1f),
                DepthMap = new Vector4(projection.M33, projection.M34, projection.M43, WorldScale.ToWorld(1f)),
            };

            var previousTargets = new ID3D11RenderTargetView[1];
            context.OMGetRenderTargets(1, previousTargets, out var previousDepth);

            try
            {
                DrawMask(render, item, targets, drawSource);

                var states = RenderStates.For(render.Device);

                context.OMSetBlendState(null);
                context.OMSetDepthStencilState(states.DepthDisabled);
                context.RSSetState(states.CullNone);
                context.RSSetViewport(new Viewport(0, 0, width, height));
                context.IASetInputLayout(null);
                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
                context.VSSetShader(vertexShader);
                context.VSSetConstantBuffer(2, constantBuffer);
                context.PSSetConstantBuffer(2, constantBuffer);
                context.PSSetSampler(3, states.LinearSampler);

                context.PSSetShaderResource(4, targets.MaskView);
                context.PSSetShaderResource(5, targets.DepthView);

                var written = Seed(context, targets, constants);
                written = Spread(context, targets, constants, written, reach, region);

                context.OMSetRenderTargets(previousTargets[0], previousDepth);
                context.RSSetViewports(viewports);
                context.OMSetBlendState(states.GetBlend(item.Blend));
                context.OMSetDepthStencilState(item.IsAlwaysOnTop ? states.DepthDisabled : states.DepthDefault);

                constants.Target = new Vector4(viewport.X, viewport.Y, width, height);
                constants.StrokeColor.W *= item.Opacity;
                context.UpdateSubresource(in constants, constantBuffer);

                context.PSSetShaderResource(6, written.View);
                context.PSSetShaderResource(7, look.Brush!);
                context.PSSetShader(compositeShader);
                context.Draw(4, 0);
            }
            finally
            {
                for (var slot = 4; slot <= 7; slot++)
                    context.PSSetShaderResource(slot, null!);

                context.OMSetRenderTargets(previousTargets[0], previousDepth);
                previousTargets[0]?.Dispose();
                previousDepth?.Dispose();
                context.RSSetViewports(viewports);
                context.OMSetBlendState(null);
                context.OMSetDepthStencilState(null);
                context.RSSetState(null);
            }
        }

        private static void DrawMask(in Render3DContext render, DrawContext3D item, Targets targets, SourceDraw drawSource)
        {
            var context = render.Context;

            context.OMSetRenderTargets(targets.MaskTarget, targets.DepthTarget);
            context.ClearRenderTargetView(targets.MaskTarget, new Color4(0f, 0f, 0f, 0f));
            context.ClearDepthStencilView(targets.DepthTarget, DepthStencilClearFlags.Depth, 1f, 0);
            context.RSSetViewport(new Viewport(0, 0, targets.Width, targets.Height));

            drawSource(render, new DrawContext3D
            {
                World = item.World,
                Opacity = 1f,
                Time = item.Time,
                Texture = item.Texture,
            });

            context.OMSetRenderTargets((ID3D11RenderTargetView)null!, (ID3D11DepthStencilView?)null);
        }

        private static SeedTarget Seed(ID3D11DeviceContext context, Targets targets, OutlineConstants constants)
        {
            var target = targets.SeedsA;

            context.UpdateSubresource(in constants, targets.Owner.constantBuffer);
            context.PSSetShaderResource(6, null!);
            context.OMSetRenderTargets(target.Target, (ID3D11DepthStencilView?)null);
            context.PSSetShader(targets.Owner.seedShader);
            context.Draw(4, 0);

            return target;
        }

        private static SeedTarget Spread(
            ID3D11DeviceContext context, Targets targets, OutlineConstants constants, SeedTarget written, float reach, Vector4 region)
        {
            var longest = MathF.Max(region.Z - region.X, region.W - region.Y);
            var limit = MathF.Min(reach, longest);
            var step = 1;

            while (step * 2 <= limit)
                step *= 2;

            context.PSSetShader(targets.Owner.jumpShader);

            for (var pass = step; ; pass /= 2)
            {
                written = Jump(context, targets, constants, written, pass);

                if (pass <= 1)
                    break;
            }

            return Jump(context, targets, constants, written, 1);
        }

        private static SeedTarget Jump(
            ID3D11DeviceContext context, Targets targets, OutlineConstants constants, SeedTarget from, int step)
        {
            var to = ReferenceEquals(from, targets.SeedsA) ? targets.SeedsB : targets.SeedsA;

            constants.Stroke.W = step;
            context.UpdateSubresource(in constants, targets.Owner.constantBuffer);

            context.PSSetShaderResource(6, null!);
            context.OMSetRenderTargets(to.Target, (ID3D11DepthStencilView?)null);
            context.PSSetShaderResource(6, from.View);
            context.Draw(4, 0);

            return to;
        }

        private static bool TryMeasure(
            in Render3DContext render,
            in Matrix4x4 world,
            WorldBounds bounds,
            int width,
            int height,
            out Vector2 center,
            out float pixelsPerItemPixel,
            out Vector2[]? corners)
        {
            center = new Vector2(width, height) / 2f;
            pixelsPerItemPixel = 0f;
            corners = null;

            var worldViewProjection = world * render.View * render.Projection;
            var middle = Vector4.Transform(new Vector4(bounds.Center, 1f), worldViewProjection);

            if (middle.W <= 1e-4f)
                return false;

            center = ToPixel(middle, width, height);
            pixelsPerItemPixel = WorldScale.ToWorld(1f) * MathF.Abs(render.Projection.M22) * height / 2f / middle.W;

            if (!(pixelsPerItemPixel > 0f) || !float.IsFinite(pixelsPerItemPixel))
                return false;

            Span<Vector3> points = stackalloc Vector3[WorldBounds.CornerCount];
            bounds.WriteCorners(points);

            var projected = new Vector2[WorldBounds.CornerCount];

            for (var i = 0; i < points.Length; i++)
            {
                var clip = Vector4.Transform(new Vector4(points[i], 1f), worldViewProjection);

                if (clip.W <= 1e-4f)
                    return true;

                projected[i] = ToPixel(clip, width, height);
            }

            corners = projected;
            return true;
        }

        private static Vector2 ToPixel(Vector4 clip, int width, int height)
            => new((clip.X / clip.W + 1f) / 2f * width, (1f - clip.Y / clip.W) / 2f * height);

        private static Vector4 MeasureRegion(
            Vector2[]? corners, Vector2 center, Vector2 offset, in OutlineLook look, float reach, int width, int height)
        {
            if (corners is null)
                return new Vector4(0f, 0f, width, height);

            var angle = float.DegreesToRadians(look.Rotation);
            var forward = Matrix3x2.CreateScale(look.Zoom) * Matrix3x2.CreateRotation(angle);

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var corner in corners)
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);

                var moved = center + offset + Vector2.Transform(corner - center, forward);
                min = Vector2.Min(min, moved);
                max = Vector2.Max(max, moved);
            }

            min = Vector2.Clamp(Floor(min - new Vector2(reach)), Vector2.Zero, new Vector2(width, height));
            max = Vector2.Clamp(Ceiling(max + new Vector2(reach)), Vector2.Zero, new Vector2(width, height));

            return new Vector4(min, max.X, max.Y);
        }

        private static Vector2 Floor(Vector2 value) => new(MathF.Floor(value.X), MathF.Floor(value.Y));

        private static Vector2 Ceiling(Vector2 value) => new(MathF.Ceiling(value.X), MathF.Ceiling(value.Y));

        private T Collect<T>(T resource) where T : IDisposable
        {
            disposer.Collect(resource);
            return resource;
        }

        private Targets Recreate(int width, int height)
        {
            targets?.Dispose();
            return new Targets(this, width, height);
        }

        public void Dispose()
        {
            targets?.Dispose();
            targets = null;
            disposer.Dispose();
        }

        private sealed record SeedTarget(ID3D11RenderTargetView Target, ID3D11ShaderResourceView View);

        private sealed class Targets : IDisposable
        {
            private readonly DisposeCollector disposer = new();

            public Targets(OutlinePass owner, int width, int height)
            {
                Owner = owner;
                Width = width;
                Height = height;

                var device = owner.device;

                var mask = Collect(device.CreateTexture2D(Describe(width, height, Format.B8G8R8A8_UNorm, BindFlags.RenderTarget)));
                MaskTarget = Collect(device.CreateRenderTargetView(mask));
                MaskView = Collect(device.CreateShaderResourceView(mask));

                var depth = Collect(device.CreateTexture2D(Describe(width, height, Format.R32_Typeless, BindFlags.DepthStencil)));
                DepthTarget = Collect(device.CreateDepthStencilView(depth, new DepthStencilViewDescription(DepthStencilViewDimension.Texture2D, Format.D32_Float)));
                DepthView = Collect(device.CreateShaderResourceView(depth, new ShaderResourceViewDescription(depth, Vortice.Direct3D.ShaderResourceViewDimension.Texture2D, Format.R32_Float)));

                SeedsA = CreateSeeds(device, width, height);
                SeedsB = CreateSeeds(device, width, height);
            }

            public OutlinePass Owner { get; }

            public int Width { get; }

            public int Height { get; }

            public ID3D11RenderTargetView MaskTarget { get; }

            public ID3D11ShaderResourceView MaskView { get; }

            public ID3D11DepthStencilView DepthTarget { get; }

            public ID3D11ShaderResourceView DepthView { get; }

            public SeedTarget SeedsA { get; }

            public SeedTarget SeedsB { get; }

            private SeedTarget CreateSeeds(ID3D11Device device, int width, int height)
            {
                var texture = Collect(device.CreateTexture2D(Describe(width, height, Format.R32G32B32A32_Float, BindFlags.RenderTarget)));

                return new SeedTarget(
                    Collect(device.CreateRenderTargetView(texture)),
                    Collect(device.CreateShaderResourceView(texture)));
            }

            private static Texture2DDescription Describe(int width, int height, Format format, BindFlags bind) => new()
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = bind | BindFlags.ShaderResource,
            };

            private T Collect<T>(T resource) where T : IDisposable
            {
                disposer.Collect(resource);
                return resource;
            }

            public void Dispose() => disposer.Dispose();
        }
    }
}
