using System.Numerics;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Backlight3D
{
    internal sealed class Backlight3DSource(IGraphicsDevicesAndContext devices, Backlight3DParameter parameter) : Shape3DSourceBase(devices)
    {
        private const int DepthSlot = 4;

        private readonly Backlight3DParameter parameter = parameter;
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines = new(device => CreatePipeline(device, "BacklightPixel.hlsl"));
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> spreads = new(device => CreatePipeline(device, "BacklightSpread.hlsl"));

        private Scratch? scratch;

        private static RenderPipeline<TransformConstants> CreatePipeline(ID3D11Device device, string pixelShader)
            => new(device, new PlaneMesh(device), new ShaderMaterial(device, typeof(Backlight3DSource).Assembly, "BacklightVertex.hlsl", "VSMain", pixelShader, "main"));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (item.DepthOnly || render.IsShadowPass)
                return;

            if (!pipelines.TryGet(render.Device, out var pipeline))
                return;

            var time = item.Time;
            var density = parameter.Density.GetFloat(time) / 100f;

            if (density <= 0f)
                return;

            var box = parameter.GetLocalMatrix(time) * item.World;

            if (!Matrix4x4.Invert(box, out var inside))
                return;

            if (!Matrix4x4.Invert(render.ViewProjection, out var unproject))
                return;

            var reach = Reach(box);

            if (reach <= 0f)
                return;

            var steps = Math.Clamp(parameter.Slices, Backlight3DParameter.LeastSlices, Backlight3DParameter.MostSlices);
            var shrink = parameter.Detail.Divisor();

            var constants = new BacklightConstants
            {
                BoxInverse = Matrix4x4.Transpose(inside),
                Unproject = Matrix4x4.Transpose(unproject),
                Tint = new Vector4(parameter.Tint.ToVector3(), density * 2.5f / (reach * 2f)),
                Air = new Vector4(parameter.EdgeBlur.GetFloat(time) / 100f, parameter.Forward.GetFloat(time) / 100f * 0.92f, parameter.Bloom.GetFloat(time) / 100f, item.Opacity),
                Walk = new Vector4(steps, reach, render.SceneDepth is null ? 0f : 1f, shrink),
            };

            var settings = new DrawSettings
            {
                Blend = BlendMode.Accumulate,
                Culling = FaceCulling.None,
                IgnoreDepth = true,
                SkipDepthWrite = true,
            };

            var context = render.Context;
            var count = context.RSGetViewports();
            var viewports = count > 0 ? new Viewport[count] : [];

            if (count > 0)
                context.RSGetViewports(viewports);

            var wide = viewports.Length > 0 ? (int)viewports[0].Width : 0;
            var tall = viewports.Length > 0 ? (int)viewports[0].Height : 0;

            var targets = new ID3D11RenderTargetView[1];

            context.OMGetRenderTargets(1, targets, out var depthStencil);

            try
            {
                var small = shrink > 1 && wide > 0 && tall > 0
                    ? Reserve(render.Device, Math.Max(wide / shrink, 1), Math.Max(tall / shrink, 1))
                    : null;

                if (small is null)
                {
                    context.OMSetRenderTargets(targets[0], null);
                    context.PSSetShaderResource(DepthSlot, render.SceneDepth!);

                    pipeline.Draw(context, render.CreateConstants(Matrix4x4.Identity, item.Opacity, unlit: true), constants with { Walk = constants.Walk with { W = 1f } }, settings);
                }
                else
                {
                    context.OMSetRenderTargets(small.Target, null);
                    context.RSSetViewport(0, 0, small.Wide, small.Tall);
                    context.ClearRenderTargetView(small.Target, new Color4(0f, 0f, 0f, 0f));
                    context.PSSetShaderResource(DepthSlot, render.SceneDepth!);

                    pipeline.Draw(context, render.CreateConstants(Matrix4x4.Identity, item.Opacity, unlit: true), constants, settings);

                    context.PSSetShaderResource(DepthSlot, null!);
                    context.OMSetRenderTargets(targets[0], null);

                    if (viewports.Length > 0)
                        context.RSSetViewports(viewports);

                    context.PSSetShaderResource(DepthSlot, small.View);

                    if (spreads.TryGet(render.Device, out var spread))
                        spread.Draw(context, render.CreateConstants(Matrix4x4.Identity, 1f, unlit: true), constants, settings);
                }
            }
            finally
            {
                context.PSSetShaderResource(DepthSlot, null!);
                context.OMSetRenderTargets(targets[0], depthStencil);

                if (viewports.Length > 0)
                    context.RSSetViewports(viewports);

                targets[0]?.Dispose();
                depthStencil?.Dispose();
            }
        }

        private Scratch Reserve(ID3D11Device device, int wide, int tall)
        {
            if (scratch is { } held && held.Wide == wide && held.Tall == tall && held.Device == device)
                return held;

            scratch?.Dispose();

            return scratch = Scratch.Create(device, wide, tall);
        }

        private static float Reach(in Matrix4x4 box)
        {
            var center = Vector3.Transform(Vector3.Zero, box);
            var corner = Vector3.Transform(new Vector3(0.5f, 0.5f, 0.5f), box) - center;
            var other = Vector3.Transform(new Vector3(-0.5f, 0.5f, 0.5f), box) - center;

            return MathF.Max(corner.Length(), other.Length());
        }

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            var size = parameter.GetSize(itemTime) * 0.5f;
            var turn = parameter.GetTurn(itemTime);

            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);

            for (var i = 0; i < 8; i++)
            {
                var corner = Vector3.Transform(new Vector3((i & 1) == 0 ? -size.X : size.X, (i & 2) == 0 ? -size.Y : size.Y, (i & 4) == 0 ? -size.Z : size.Z), turn);

                low = Vector3.Min(low, corner);
                high = Vector3.Max(high, corner);
            }

            return new WorldBounds(low, high);
        }

        public override void Dispose()
        {
            scratch?.Dispose();
            scratch = null;
            spreads.Dispose();
            pipelines.Dispose();
            base.Dispose();
        }

        private sealed class Scratch(ID3D11Device device, ID3D11Texture2D texture, int wide, int tall) : IDisposable
        {
            public ID3D11Device Device { get; } = device;

            public int Wide { get; } = wide;

            public int Tall { get; } = tall;

            public ID3D11RenderTargetView Target { get; } = device.CreateRenderTargetView(texture);

            public ID3D11ShaderResourceView View { get; } = device.CreateShaderResourceView(texture);

            public static Scratch Create(ID3D11Device device, int wide, int tall)
            {
                var texture = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = wide,
                    Height = tall,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R16G16B16A16_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                });

                return new Scratch(device, texture, wide, tall);
            }

            public void Dispose()
            {
                View.Dispose();
                Target.Dispose();
                texture.Dispose();
            }
        }
    }
}
