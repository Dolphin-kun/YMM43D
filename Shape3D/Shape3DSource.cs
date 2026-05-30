using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YMM43D.Commons;

namespace Shape3D
{
    internal class Shape3DSource : IShapeSource2
    {
        private readonly IGraphicsDevicesAndContext devices;
        private readonly Shape3DParameter parameter;

        private readonly DisposeCollector disposer = new();
        private readonly D3D11RenderSurface surface;
        private readonly DeviceResourceCache<CubeResources> resourceCache;
        private double size;

        private ID2D1CommandList? commandList;
        public ID2D1Image Output => commandList ?? throw new Exception("画像が未生成です");
        public IEnumerable<VideoController> Controllers => [];

        public Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter)
        {
            this.devices = devices;
            this.parameter = parameter;
            surface = new D3D11RenderSurface();
            disposer.Collect(surface);
            resourceCache = new DeviceResourceCache<CubeResources>(device => new CubeResources(device));
            disposer.Collect(resourceCache);
            SharedGraphics.Devices = devices;
        }

        public void Update(TimelineItemSourceDescription timelineItemSourceDescription)
        {
            var fps = timelineItemSourceDescription.FPS;
            var frame = timelineItemSourceDescription.ItemPosition.Frame;
            var length = timelineItemSourceDescription.ItemDuration.Frame;

            double size = parameter.Size.GetValue(frame, length, fps);
            if (size <= 0) { commandList?.Dispose(); commandList = null; return; }

            int renderSize = (int)(size * 2);
            SharedGraphics.AcquireIndependentDevice(out var d3d3D, out var d3dDc);
            try
            {
                var res = resourceCache.Get(d3d3D);
                lock (d3d3D)
                {
                    if (this.size != size) { surface.Recreate(devices, renderSize, renderSize); this.size = size; }
                    if (surface.RenderTargetView == null) return;

                    var rotation = YMM43D.Commons.Math.CreateObjectRotation(
                        (float)parameter.RX.GetValue(frame, length, fps),
                        (float)parameter.RY.GetValue(frame, length, fps),
                        (float)parameter.RZ.GetValue(frame, length, fps)
                    );
                    
                    var sceneCamera = YMM43D.Preview.SceneCamera.Instance;
                    Matrix4x4 view = sceneCamera.GetViewMatrix(timelineItemSourceDescription);
                    Matrix4x4 proj = YMM43D.Preview.SceneCamera.GetProjectionMatrix(1.0f);

                    var oldRTVs = new ID3D11RenderTargetView[1];
                    d3dDc.OMGetRenderTargets(1, oldRTVs, out ID3D11DepthStencilView? oldDSV);
                    ID3D11RenderTargetView? oldRTV = oldRTVs[0];

                    try
                    {
                        d3dDc.OMSetRenderTargets(surface.RenderTargetView, surface.DepthStencilView);
                        d3dDc.ClearRenderTargetView(surface.RenderTargetView, new Color4(0, 0, 0, 0));
                        if (surface.DepthStencilView != null) d3dDc.ClearDepthStencilView(surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
                        
                        d3dDc.RSSetViewport(new Viewport(0, 0, renderSize, renderSize));
                        
                        DrawInternal(d3d3D, d3dDc, Matrix4x4.CreateScale(2.0f) * rotation * view, proj, 1.0f, YukkuriMovieMaker.Project.Blend.Normal, false, false, true, res);
                    }
                    finally
                    {
                        d3dDc.OMSetRenderTargets(oldRTV, oldDSV);
                        oldRTV?.Dispose();
                        oldDSV?.Dispose();
                    }
                    
                    d3dDc.Flush();
                }
            }
            finally
            {
                SharedGraphics.ReleaseIndependentDevice();
            }

            var d2dDc = devices.DeviceContext;
            lock (devices)
            {
                commandList?.Dispose();
                commandList = d2dDc.CreateCommandList();
                d2dDc.Target = commandList;
                d2dDc.BeginDraw();
                d2dDc.Clear(null);
                if (surface.Bitmap != null) {
                    float halfSize = renderSize / 2f;
                    d2dDc.DrawImage(surface.Bitmap, new Vector2(-halfSize, -halfSize), null, Vortice.Direct2D1.InterpolationMode.Linear, CompositeMode.SourceOver);
                }
                d2dDc.EndDraw();
                d2dDc.Target = null;
                commandList.Close();
            }
        }

        public void Draw(ID3D11Device device, ID3D11DeviceContext d3dDc, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            var res = resourceCache.Get(device);

            var localRotation = YMM43D.Commons.Math.CreateObjectRotation(
                (float)parameter.RX.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS),
                (float)parameter.RY.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS),
                (float)parameter.RZ.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS)
            );
            
            var finalWorld = Matrix4x4.CreateScale(2.0f) * localRotation * drawContext.World;
            
            DrawInternal(device, d3dDc, finalWorld * view, projection, drawContext.Opacity, drawContext.Blend, drawContext.IsInverted, drawContext.IsAlwaysOnTop, drawContext.IsZOrderEnabled, res);
        }

        private static void DrawInternal(ID3D11Device device, ID3D11DeviceContext d3dDc, Matrix4x4 viewWorld, Matrix4x4 projection, float opacity, YukkuriMovieMaker.Project.Blend blend, bool inverted, bool alwaysOnTop, bool zOrder, CubeResources res)
        {
            var wvpMatrix = viewWorld * projection;
            var data = new CubeResources.ConstantData
            {
                WorldViewProjection = Matrix4x4.Transpose(wvpMatrix),
                Opacity = opacity
            };
            d3dDc.UpdateSubresource(in data, res.ConstantBuffer);
            
            var depthStates = res.DepthStencilStates.Get(device);
            if (alwaysOnTop) 
                d3dDc.OMSetDepthStencilState(depthStates.NoDepth);
            else 
                d3dDc.OMSetDepthStencilState(depthStates.Default);

            var blendStates = res.BlendStates.Get(device);
            ID3D11BlendState blendState = blend switch
            {
                YukkuriMovieMaker.Project.Blend.Add => blendStates.Add,
                YukkuriMovieMaker.Project.Blend.Subtract => blendStates.Subtract,
                YukkuriMovieMaker.Project.Blend.Multiply => blendStates.Multiply,
                YukkuriMovieMaker.Project.Blend.Screen => blendStates.Screen,
                _ => blendStates.Normal
            };
            d3dDc.OMSetBlendState(blendState);

            d3dDc.VSSetShader(res.Material.VertexShader);
            d3dDc.PSSetShader(res.Material.PixelShader);
            d3dDc.IASetInputLayout(res.InputLayout);
            d3dDc.IASetVertexBuffer(0, res.Geometry.VertexBuffer, Marshal.SizeOf<Vertex>(), 0);
            d3dDc.IASetIndexBuffer(res.Geometry.IndexBuffer, Format.R16_UInt, 0);
            d3dDc.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            d3dDc.VSSetConstantBuffer(0, res.ConstantBuffer);
            d3dDc.PSSetConstantBuffer(0, res.ConstantBuffer);

            var rasterStates = res.RasterizerStates.Get(device);

            // デバッグ用: カリングを無効化して1パスで描画
            d3dDc.RSSetState(rasterStates.CullNone);
            d3dDc.DrawIndexed(res.Geometry.IndexCount, 0, 0);

            d3dDc.OMSetBlendState(null);
            d3dDc.OMSetDepthStencilState(null);
            d3dDc.RSSetState(null);
            d3dDc.Flush();
        }

        public void Dispose()
        {
            disposer.Dispose();
            commandList?.Dispose();
        }
    }
}
