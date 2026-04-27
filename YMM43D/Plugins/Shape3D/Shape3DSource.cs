using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Rendering;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;
using YMM43D.Rendering.States;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Plugins.Shape3D
{
    internal class Shape3DSource : IShape3DSource
    {
        private readonly IGraphicsDevicesAndContext devices;
        private readonly Shape3DParameter parameter;

        private readonly D3D11RenderSurface surface = new();
        private readonly DeviceResourceCache<CubeResources> resourceCache;
        private double size;

        private ID2D1CommandList? commandList;
        public ID2D1Image Output => commandList ?? throw new Exception("画像が未生成です");
        public IEnumerable<VideoController> Controllers => [];

        public Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter)
        {
            this.devices = devices;
            this.parameter = parameter;
            resourceCache = new DeviceResourceCache<CubeResources>(device => new CubeResources(device));
            ProviderRegistry.Register(parameter, this);
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
            var d3d3D = SharedGraphics.IndependentDevice;
            var d3dDc = SharedGraphics.IndependentContext;
            
            var res = resourceCache.Get(d3d3D);
            lock (d3d3D)
            {
                if (this.size != size) { surface.Recreate(devices, renderSize, renderSize); this.size = size; }
                if (surface.RenderTargetView == null) return;

                var rx = (float)(parameter.RX.GetValue(frame, length, fps) * Math.PI / 180.0);
                var ry = (float)(parameter.RY.GetValue(frame, length, fps) * Math.PI / 180.0);
                var rz = (float)(parameter.RZ.GetValue(frame, length, fps) * Math.PI / 180.0);
                var rotation = Matrix4x4.CreateRotationX(rx) * Matrix4x4.CreateRotationY(ry) * Matrix4x4.CreateRotationZ(rz);
                
                var sceneCamera = Preview.SceneCamera.Instance;
                Matrix4x4 view = sceneCamera.GetViewMatrix();
                Matrix4x4 proj = sceneCamera.GetProjectionMatrix(1.0f); // メインプレビュー用アスペクト比

                // 現在のレンダーターゲットを保存
                var oldRTVs = new ID3D11RenderTargetView[1];
                ID3D11DepthStencilView? oldDSV;
                d3dDc.OMGetRenderTargets(1, oldRTVs, out oldDSV);
                ID3D11RenderTargetView? oldRTV = oldRTVs[0];

                try
                {
                    d3dDc.OMSetRenderTargets(surface.RenderTargetView, surface.DepthStencilView);
                    d3dDc.ClearRenderTargetView(surface.RenderTargetView, new Color4(0, 0, 0, 0));
                    if (surface.DepthStencilView != null) d3dDc.ClearDepthStencilView(surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
                    
                    d3dDc.RSSetViewport(new Viewport(0, 0, renderSize, renderSize));
                    
                    DrawInternal(d3dDc, rotation * view, proj, 1.0f, YukkuriMovieMaker.Project.Blend.Normal, false, false, true, res);
                }
                finally
                {
                    // 元のレンダーターゲットに戻す
                    d3dDc.OMSetRenderTargets(oldRTV, oldDSV);
                    oldRTV?.Dispose();
                    oldDSV?.Dispose();
                }
                
                d3dDc.Flush();
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

        public void Draw(ID3D11DeviceContext d3dDc, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            var res = resourceCache.Get(d3dDc.Device);

            var rx = (float)(parameter.RX.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) * Math.PI / 180.0);
            var ry = (float)(parameter.RY.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) * Math.PI / 180.0);
            var rz = (float)(parameter.RZ.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) * Math.PI / 180.0);
            var localRotation = Matrix4x4.CreateRotationX(rx) * Matrix4x4.CreateRotationY(ry) * Matrix4x4.CreateRotationZ(rz);
            
            var finalWorld = localRotation * drawContext.World;
            
            DrawInternal(d3dDc, finalWorld * view, projection, drawContext.Opacity, drawContext.Blend, drawContext.IsInverted, drawContext.IsAlwaysOnTop, drawContext.IsZOrderEnabled, res);
        }

        private static void DrawInternal(ID3D11DeviceContext d3dDc, Matrix4x4 viewWorld, Matrix4x4 projection, float opacity, YukkuriMovieMaker.Project.Blend blend, bool inverted, bool alwaysOnTop, bool zOrder, CubeResources res)
        {
            var wvpMatrix = viewWorld * projection;
            var data = new CubeResources.ConstantData
            {
                WorldViewProjection = Matrix4x4.Transpose(wvpMatrix),
                Opacity = opacity
            };
            d3dDc.UpdateSubresource(in data, res.ConstantBuffer);
            
            var depthStates = res.DepthStencilStates.Get(d3dDc.Device);
            if (alwaysOnTop) 
                d3dDc.OMSetDepthStencilState(depthStates.NoDepth);
            else 
                d3dDc.OMSetDepthStencilState(depthStates.Default);

            var blendStates = res.BlendStates.Get(d3dDc.Device);
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

            var rasterStates = res.RasterizerStates.Get(d3dDc.Device);

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
            resourceCache.Dispose();
            surface.Dispose();
            commandList?.Dispose();
        }
    }
}
