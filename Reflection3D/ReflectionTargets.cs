using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace Reflection3D
{
    internal sealed class ReflectionTargets(ID3D11Device device, int slices) : IDisposable
    {
        private const int Granularity = 128;
        private const int MaxSize = 4096;

        private DisposeCollector? made;
        private ID3D11RenderTargetView[] colorTargets = [];
        private ID3D11DepthStencilView[] depthTargets = [];

        public ID3D11ShaderResourceView? Color { get; private set; }

        public ID3D11ShaderResourceView? Depth { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public int MipLevels { get; private set; }

        public void Ensure(int width, int height)
        {
            width = Math.Clamp(width, 1, MaxSize);
            height = Math.Clamp(height, 1, MaxSize);

            if (made is not null && Width >= width && Height >= height)
                return;

            Release();

            Width = Math.Min((Math.Max(width, Width) + Granularity - 1) / Granularity * Granularity, MaxSize);
            Height = Math.Min((Math.Max(height, Height) + Granularity - 1) / Granularity * Granularity, MaxSize);
            MipLevels = (int)MathF.Floor(MathF.Log2(Math.Max(Width, Height))) + 1;

            var collector = made = new DisposeCollector();

            var color = collector.CollectEx(device.CreateTexture2D(new Texture2DDescription
            {
                Width = Width,
                Height = Height,
                MipLevels = MipLevels,
                ArraySize = slices,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.GenerateMips,
            }));

            var depth = collector.CollectEx(device.CreateTexture2D(new Texture2DDescription
            {
                Width = Width,
                Height = Height,
                MipLevels = 1,
                ArraySize = slices,
                Format = Format.R24G8_Typeless,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
            }));

            colorTargets = new ID3D11RenderTargetView[slices];
            depthTargets = new ID3D11DepthStencilView[slices];

            for (var i = 0; i < slices; i++)
            {
                var colorSlice = new RenderTargetViewDescription
                {
                    Format = Format.B8G8R8A8_UNorm,
                    ViewDimension = RenderTargetViewDimension.Texture2DArray,
                    Texture2DArray = new Texture2DArrayRenderTargetView { MipSlice = 0, FirstArraySlice = i, ArraySize = 1 },
                };

                var depthSlice = new DepthStencilViewDescription
                {
                    Format = Format.D24_UNorm_S8_UInt,
                    ViewDimension = DepthStencilViewDimension.Texture2DArray,
                    Texture2DArray = new Texture2DArrayDepthStencilView { MipSlice = 0, FirstArraySlice = i, ArraySize = 1 },
                };

                colorTargets[i] = collector.CollectEx(device.CreateRenderTargetView(color, colorSlice));
                depthTargets[i] = collector.CollectEx(device.CreateDepthStencilView(depth, depthSlice));
            }

            Color = collector.CollectEx(device.CreateShaderResourceView(color, new ShaderResourceViewDescription
            {
                Format = Format.B8G8R8A8_UNorm,
                ViewDimension = ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayShaderResourceView { MostDetailedMip = 0, MipLevels = MipLevels, FirstArraySlice = 0, ArraySize = slices },
            }));

            Depth = collector.CollectEx(device.CreateShaderResourceView(depth, new ShaderResourceViewDescription
            {
                Format = Format.R24_UNorm_X8_Typeless,
                ViewDimension = ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayShaderResourceView { MostDetailedMip = 0, MipLevels = 1, FirstArraySlice = 0, ArraySize = slices },
            }));
        }

        public void Render(
            int slice,
            in Render3DContext render,
            in Matrix4x4 view,
            in Matrix4x4 projection,
            int width,
            int height,
            IReadOnlyCollection<object?> selves,
            bool mirrored)
        {
            var context = render.Context;
            var viewportCount = context.RSGetViewports();
            var viewports = new Viewport[Math.Max(viewportCount, 1)];

            if (viewportCount > 0)
                context.RSGetViewports(viewports);

            var previousTargets = new ID3D11RenderTargetView[1];
            context.OMGetRenderTargets(1, previousTargets, out var previousDepth);

            var capturing = render with { View = view, Projection = projection, IsCapturingScene = true };
            var wasMirrored = RenderStates.IsMirrored;

            try
            {
                context.OMSetRenderTargets(colorTargets[slice], depthTargets[slice]);
                context.ClearRenderTargetView(colorTargets[slice], new Color4(0f, 0f, 0f, 0f));
                context.ClearDepthStencilView(depthTargets[slice], DepthStencilClearFlags.Depth, 1f, 0);
                context.RSSetViewport(new Viewport(0, 0, width, height));

                RenderStates.IsMirrored = mirrored;

                foreach (var occluder in render.Scene)
                {
                    if (selves.Any(self => ReferenceEquals(occluder.Provider, self)))
                        continue;

                    if (occluder.Provider is I3DBounds bounded && capturing.IsOutside(bounded.GetLocalBounds(occluder.Time), occluder.World))
                        continue;

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
                RenderStates.IsMirrored = wasMirrored;
                context.OMSetRenderTargets(previousTargets[0], previousDepth);
                previousTargets[0]?.Dispose();
                previousDepth?.Dispose();

                if (viewportCount > 0)
                    context.RSSetViewports(viewports);
            }
        }

        public void Finish(ID3D11DeviceContext context)
        {
            if (Color is not null)
                context.GenerateMips(Color);
        }

        public void Dispose() => Release();

        private void Release()
        {
            made?.Dispose();
            made = null;
            colorTargets = [];
            depthTargets = [];
            Color = null;
            Depth = null;
        }
    }
}
